using System.Diagnostics;

using Microsoft.Extensions.Time.Testing;

namespace MicroKit.Messaging.UnitTests.Publishing;

using MicroKit.Messaging.Outbox;
using MicroKit.Messaging.Publishing;
using MicroKit.Messaging.Serialization;

/// <summary>
/// Unit tests for the publisher. No database: it writes through a port, which is why splitting it
/// from the writer was worth doing.
/// </summary>
public sealed class IntegrationEventPublisherTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 9, 14, 22, TimeSpan.Zero);

    private const string Contract = "saasbtp.safety.constat-recorded.v1";

    /// <summary>
    /// The guard runs before any other work, and this asserts both halves of that.
    /// </summary>
    /// <remarks>
    /// "It threw" is not the property. A guard placed after the write would also throw, while
    /// leaving the row committed by the provider's implicit transaction — an integration event
    /// announced permanently for a fact the caller may still roll back. Asserting that nothing
    /// reached the writer is what rules that out.
    /// </remarks>
    [Fact]
    public async Task PublishAsync_WithNoOpenTransaction_ThrowsAndStagesNothing()
    {
        var writer = new RecordingWriter { HasOpenTransaction = false };
        var publisher = Build(writer);

        await Should.ThrowAsync<IntegrationEventPublishException>(
            async () => await publisher.PublishAsync(new ConstatRecorded(Guid.NewGuid())));

        writer.Written.ShouldBeEmpty();
    }

    [Fact]
    public async Task PublishAsync_WithNoOpenTransaction_NamesTheUnitOfWorkRequirement()
    {
        var publisher = Build(new RecordingWriter { HasOpenTransaction = false });

        var exception = await Should.ThrowAsync<IntegrationEventPublishException>(
            async () => await publisher.PublishAsync(new ConstatRecorded(Guid.NewGuid())));

        // The likeliest mistake is calling CommitAsync and believing that opened a transaction.
        // The message has to say so, because the failure is otherwise indistinguishable from a
        // wiring problem.
        exception.Message.ShouldContain(nameof(ConstatRecorded));
        exception.Message.ShouldContain("ITransactionalContext.ExecuteAsync");
    }

    [Fact]
    public async Task PublishAsync_WhenEventNotRegistered_ThrowsAndNamesTheType()
    {
        var publisher = Build(new RecordingWriter());

        var exception = await Should.ThrowAsync<IntegrationEventConfigurationException>(
            async () => await publisher.PublishAsync(new NeverRegistered()));

        exception.Message.ShouldContain(nameof(NeverRegistered));
    }

    /// <summary>
    /// The whole shape of a contract row in one assertion, origin included — the kind is what both
    /// dispatchers route on, and the origin is half the replay key.
    /// </summary>
    [Fact]
    public async Task PublishAsync_StagesAContractRow_WithKindContractNameSourceAndOrigin()
    {
        var writer = new RecordingWriter();
        var origin = MessageId.New();
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();

        var publisher = Build(
            writer,
            context: new StubExecutionContext(
                "org_7f3a", correlationId.ToString(), causationId.ToString()),
            origin: origin);

        var id = await publisher.PublishAsync(new ConstatRecorded(Guid.NewGuid()));

        var row = writer.Written.ShouldHaveSingleItem();

        row.Id.ShouldBe(id);
        row.MessageKind.ShouldBe(MessageKind.Contract);
        row.ContractName.ShouldBe(Contract);
        row.Source.ShouldBe("/saasbtp/safety");
        row.OriginMessageId.ShouldBe(origin);
        row.TenantId.ShouldBe("org_7f3a");
        row.CorrelationId.Value.ShouldBe(correlationId);
        row.CausationId!.Value.ShouldBe(causationId);
        row.Status.ShouldBe(OutboxMessageStatus.Pending);
        row.DeadLettered.ShouldBeFalse();
        row.RetryCount.ShouldBe(0);

        // Not the origin's id: both rows live in one table and share a primary key column.
        row.Id.ShouldNotBe(origin);
    }

    /// <summary>
    /// Publishing from a command handler or a scheduled job happens outside any dispatch, so there
    /// is no origin to record.
    /// </summary>
    /// <remarks>
    /// Such a row does not deduplicate — nulls are distinct in the unique index — and for those
    /// callers that is correct: an HTTP request is not replayed. Deliberately not extended to an
    /// inbox handler, whose replay is real; see <c>OriginMessageHolder</c>.
    /// </remarks>
    [Fact]
    public async Task PublishAsync_OutsideADispatch_StagesANullOrigin()
    {
        var writer = new RecordingWriter();
        var publisher = Build(writer);

        await publisher.PublishAsync(new ConstatRecorded(Guid.NewGuid()));

        writer.Written.ShouldHaveSingleItem().OriginMessageId.ShouldBeNull();
    }

    /// <summary>
    /// A replayed dispatch republishes; the writer reports it and the caller sees a normal return.
    /// </summary>
    /// <remarks>
    /// The identifier returned is the EXISTING row's. Returning the one this call built would name
    /// a row that was never written, so a handler logging its publication would log a value nothing
    /// downstream has ever seen.
    /// </remarks>
    [Fact]
    public async Task PublishAsync_WhenTheWriterReportsAlreadyPublished_ReturnsTheExistingId()
    {
        var existing = MessageId.New();
        var writer = new RecordingWriter { AlreadyPublishedAs = existing };
        var publisher = Build(writer, origin: MessageId.New());

        var id = await publisher.PublishAsync(new ConstatRecorded(Guid.NewGuid()));

        id.ShouldBe(existing);
        writer.Written.ShouldHaveSingleItem()
            .Id.ShouldNotBe(existing, "the row it built was rejected; only the id it returns changes");
    }

    /// <summary>
    /// A malformed correlation id is a tracing defect. Losing an integration event over one would
    /// be the wrong trade.
    /// </summary>
    /// <remarks>
    /// It no longer degrades to null: <see cref="OutboxMessage.CorrelationId"/> is non-nullable and
    /// mapped <c>IsRequired</c>, so null is not a value the row can carry. A fresh correlation is
    /// the honest substitute — the chain is broken either way, and this way the message survives.
    /// </remarks>
    [Fact]
    public async Task PublishAsync_WhenCorrelationIdIsUnparseable_SubstitutesAFreshOne()
    {
        var writer = new RecordingWriter();
        var publisher = Build(writer, context: new StubExecutionContext("t", "not-a-guid", null));

        await publisher.PublishAsync(new ConstatRecorded(Guid.NewGuid()));

        var row = writer.Written.ShouldHaveSingleItem();

        row.CorrelationId.ShouldNotBeNull();
        row.CorrelationId.Value.ShouldNotBe(Guid.Empty);
        row.TenantId.ShouldBe("t", "the publication itself must still succeed");
    }

    /// <summary>
    /// The two timestamps must not collapse. The fact happened in the business transaction; the row
    /// is staged one relay later — minutes under load, hours after an incident.
    /// </summary>
    [Fact]
    public async Task PublishAsync_RecordsOccurrenceTimeSeparatelyFromStagingTime()
    {
        var writer = new RecordingWriter();
        var publisher = Build(writer);
        var occurredOn = Now.AddHours(-3);

        await publisher.PublishAsync(new ConstatRecorded(Guid.NewGuid()), occurredOn);

        var row = writer.Written.ShouldHaveSingleItem();

        row.OccurredOnUtc.ShouldBe(occurredOn);
        row.CreatedAtUtc.ShouldBe(Now);
    }

    /// <summary>
    /// Unstated occurrence resolves to the staging time here rather than at the transport.
    /// </summary>
    /// <remarks>
    /// <see cref="OutboxMessage.OccurredOnUtc"/> and <see cref="MessageEnvelope.OccurredOnUtc"/>
    /// are both non-nullable, so "not stated" cannot reach the row. The fallback is the same value
    /// the transport would have used; only the log line records that it was an approximation.
    /// </remarks>
    [Fact]
    public async Task PublishAsync_WhenOccurrenceTimeNotSupplied_FallsBackToTheStagingTime()
    {
        var writer = new RecordingWriter();
        var publisher = Build(writer);

        await publisher.PublishAsync(new ConstatRecorded(Guid.NewGuid()));

        var row = writer.Written.ShouldHaveSingleItem();

        row.OccurredOnUtc.ShouldBe(Now);
        row.CreatedAtUtc.ShouldBe(Now);
    }

    /// <summary>
    /// The producing trace is current only here: the relay runs later, on another thread, under
    /// another activity, so nothing downstream can reconstruct it.
    /// </summary>
    [Fact]
    public async Task PublishAsync_CapturesTheTraceParent()
    {
        var writer = new RecordingWriter();
        var publisher = Build(writer);

        using var activity = new Activity("publish").Start();

        await publisher.PublishAsync(new ConstatRecorded(Guid.NewGuid()));

        writer.Written.ShouldHaveSingleItem().TraceParent.ShouldBe(activity.Id);
    }

    /// <summary>
    /// A caller holding the interface rather than the concrete type must still resolve the right
    /// contract — hence <c>GetType()</c> rather than <c>typeof(TEvent)</c> in the publisher.
    /// </summary>
    [Fact]
    public async Task PublishAsync_ResolvesTheContractFromTheRuntimeType_NotTheGenericArgument()
    {
        var writer = new RecordingWriter();
        var publisher = Build(writer);
        IIntegrationEvent asInterface = new ConstatRecorded(Guid.NewGuid());

        await publisher.PublishAsync(asInterface);

        writer.Written.ShouldHaveSingleItem().ContractName.ShouldBe(Contract);
    }

    [Fact]
    public async Task PublishAsync_SerializesTheEventIntoThePayload()
    {
        var writer = new RecordingWriter();
        var publisher = Build(writer);
        var constatId = Guid.NewGuid();

        await publisher.PublishAsync(new ConstatRecorded(constatId));

        writer.Written.ShouldHaveSingleItem().Payload.ShouldContain(constatId.ToString());
    }

    private static IIntegrationEventPublisher Build(
        RecordingWriter writer,
        StubExecutionContext? context = null,
        MessageId? origin = null)
        => new IntegrationEventPublisher(
            writer,
            IntegrationEventContractFixtures.Registry(
                ("/saasbtp/safety", e => e.Publishes<ConstatRecorded>())),
            new OutboxMessageFactory(
                new SystemTextJsonMessageSerializer(), new FakeTimeProvider(Now)),
            new OriginMessageHolder { OriginMessageId = origin },
            context ?? new StubExecutionContext(null, null, null),
            NullLogger<IntegrationEventPublisher>.Instance);

    /// <summary>
    /// A recording fake rather than a mock. A negative assertion against a mock only holds if it
    /// names a method the subject actually calls — <c>DidNotReceive().AddAsync(...)</c> against
    /// code that calls something else is green whatever happens, and no mutation can expose it.
    /// Asserting on what was recorded cannot be wrong in that way.
    /// </summary>
    private sealed class RecordingWriter : IIntegrationEventWriter
    {
        public List<OutboxMessage> Written { get; } = [];

        public bool HasOpenTransaction { get; init; } = true;

        /// <summary>When set, every write is reported as already published under this id.</summary>
        public MessageId? AlreadyPublishedAs { get; init; }

        public ValueTask<IntegrationEventWriteResult> AddAsync(
            OutboxMessage message, CancellationToken ct = default)
        {
            Written.Add(message);

            return ValueTask.FromResult(
                AlreadyPublishedAs is null
                    ? IntegrationEventWriteResult.Staged(message.Id)
                    : IntegrationEventWriteResult.AlreadyPublishedAs(AlreadyPublishedAs));
        }
    }

    private sealed record StubExecutionContext(
        string? TenantId, string? CorrelationId, string? CausationId) : IExecutionContext
    {
        public IReadOnlyDictionary<string, object?> Properties { get; } =
            new Dictionary<string, object?>();
    }
}
