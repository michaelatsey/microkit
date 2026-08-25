using Microsoft.Extensions.Time.Testing;

namespace MicroKit.Messaging.UnitTests.Publishing;

using MicroKit.Messaging.Publishing;
using MicroKit.Messaging.Serialization;

/// <summary>
/// Unit tests for the publisher. No database: it stages through a port, which is why splitting it
/// from the writer was worth doing.
/// </summary>
public sealed class IntegrationEventPublisherTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 9, 14, 22, TimeSpan.Zero);

    /// <summary>
    /// The guard runs before any other work, and this asserts both halves of that.
    /// </summary>
    /// <remarks>
    /// "It threw" is not the property. A guard placed after staging would also throw, while
    /// leaving the row for whatever transaction the caller happened to have — the event would then
    /// be published by an unrelated commit. Asserting that nothing was staged is what rules that
    /// out.
    /// </remarks>
    [Fact]
    public async Task PublishAsync_WithNoOpenTransaction_ThrowsAndStagesNothing()
    {
        var writer = new RecordingWriter { HasOpenTransaction = false };
        var publisher = Build(writer);

        await Should.ThrowAsync<IntegrationEventPublishException>(
            async () => await publisher.PublishAsync(new ConstatRecorded(Guid.NewGuid())));

        writer.Staged.ShouldBeEmpty();
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

    [Fact]
    public async Task PublishAsync_StagesTheContractTheSourceAndTheExecutionContext()
    {
        var writer = new RecordingWriter();
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();

        var publisher = Build(
            writer,
            context: new StubExecutionContext(
                "org_7f3a", correlationId.ToString(), causationId.ToString()));

        var id = await publisher.PublishAsync(new ConstatRecorded(Guid.NewGuid()));

        var row = writer.Staged.ShouldHaveSingleItem();

        row.Id.ShouldBe(id);
        row.ContractName.ShouldBe("saasbtp.safety.constat-recorded.v1");
        row.Source.ShouldBe("/saasbtp/safety");
        row.TenantId.ShouldBe("org_7f3a");
        row.CorrelationId!.Value.ShouldBe(correlationId);
        row.CausationId!.Value.ShouldBe(causationId);
        row.Status.ShouldBe(IntegrationEventStatus.Pending);
        row.DeadLettered.ShouldBeFalse();
        row.RetryCount.ShouldBe(0);
    }

    /// <summary>
    /// A malformed correlation id is a tracing defect. Losing an integration event over one would
    /// be the wrong trade, so it degrades to null rather than throwing.
    /// </summary>
    [Fact]
    public async Task PublishAsync_WhenCorrelationIdIsUnparseable_DegradesToNullRatherThanFailing()
    {
        var writer = new RecordingWriter();
        var publisher = Build(writer, context: new StubExecutionContext("t", "not-a-guid", null));

        await publisher.PublishAsync(new ConstatRecorded(Guid.NewGuid()));

        var row = writer.Staged.ShouldHaveSingleItem();

        row.CorrelationId.ShouldBeNull();
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

        var row = writer.Staged.ShouldHaveSingleItem();

        row.OccurredOnUtc.ShouldBe(occurredOn);
        row.CreatedAtUtc.ShouldBe(Now);
    }

    [Fact]
    public async Task PublishAsync_WhenOccurrenceTimeNotSupplied_LeavesItNull()
    {
        var writer = new RecordingWriter();
        var publisher = Build(writer);

        await publisher.PublishAsync(new ConstatRecorded(Guid.NewGuid()));

        var row = writer.Staged.ShouldHaveSingleItem();

        // Null means "not stated" and the transport falls back to the staging time. Defaulting it
        // here would make an approximation indistinguishable from a fact.
        row.OccurredOnUtc.ShouldBeNull();
        row.CreatedAtUtc.ShouldBe(Now);
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

        writer.Staged.ShouldHaveSingleItem()
            .ContractName.ShouldBe("saasbtp.safety.constat-recorded.v1");
    }

    [Fact]
    public async Task PublishAsync_SerializesTheEventIntoData()
    {
        var writer = new RecordingWriter();
        var publisher = Build(writer);
        var constatId = Guid.NewGuid();

        await publisher.PublishAsync(new ConstatRecorded(constatId));

        writer.Staged.ShouldHaveSingleItem().Data.ShouldContain(constatId.ToString());
    }

    private static IIntegrationEventPublisher Build(
        RecordingWriter writer,
        StubExecutionContext? context = null)
        => new IntegrationEventPublisher(
            writer,
            IntegrationEventContractFixtures.Registry(
                ("/saasbtp/safety", e => e.Publishes<ConstatRecorded>())),
            new SystemTextJsonMessageSerializer(),
            context ?? new StubExecutionContext(null, null, null),
            new FakeTimeProvider(Now),
            NullLogger<IntegrationEventPublisher>.Instance);

    /// <summary>
    /// A recording fake rather than a mock. A negative assertion against a mock only holds if it
    /// names a method the subject actually calls — <c>DidNotReceive().AddAsync(...)</c> against
    /// code that calls something else is green whatever happens, and no mutation can expose it.
    /// Asserting on what was recorded cannot be wrong in that way.
    /// </summary>
    private sealed class RecordingWriter : IIntegrationEventWriter
    {
        public List<IntegrationEventMessage> Staged { get; } = [];

        public bool HasOpenTransaction { get; init; } = true;

        public ValueTask AddAsync(IntegrationEventMessage message, CancellationToken ct = default)
        {
            Staged.Add(message);
            return ValueTask.CompletedTask;
        }
    }

    private sealed record StubExecutionContext(
        string? TenantId, string? CorrelationId, string? CausationId) : IExecutionContext
    {
        public IReadOnlyDictionary<string, object?> Properties { get; } =
            new Dictionary<string, object?>();
    }
}
