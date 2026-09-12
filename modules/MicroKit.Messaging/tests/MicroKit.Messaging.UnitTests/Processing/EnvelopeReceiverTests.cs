using Microsoft.Extensions.Time.Testing;

namespace MicroKit.Messaging.UnitTests.Processing;

using System.Diagnostics.Metrics;

using MicroKit.Messaging.Publishing;
using MicroKit.Messaging.Serialization;
using MicroKit.Messaging.UnitTests.Publishing;

/// <summary>
/// <see cref="EnvelopeReceiver"/> — the receiving seam. Turns one <see cref="MessageEnvelope"/>
/// into one <see cref="InboxMessage"/> per registered consumer.
/// </summary>
/// <remarks>
/// The three columns worth their own tests are the ones the envelope does <b>not</b> carry:
/// <c>EventType</c>, which must name a type <i>this</i> process can load; <c>ReceivedAtUtc</c>,
/// which must come from the clock and not from a caller-supplied business timestamp; and
/// <c>ConsumerType</c>, which comes from the handler registry. Everything else is a copy, and a
/// copy that is wrong shows up immediately downstream.
/// </remarks>
public sealed class EnvelopeReceiverTests
{
    private const string ContractName = "partner.billing.invoice-settled.v1";
    private const string Source = "/partner/billing";
    private const string ConsumerA = "MicroKit.Messaging.UnitTests.Processing.EnvelopeReceiverTests+FirstConsumer";
    private const string ConsumerB = "MicroKit.Messaging.UnitTests.Processing.EnvelopeReceiverTests+SecondConsumer";

    private static readonly DateTimeOffset ReceivedAt =
        new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    // Deliberately far from ReceivedAt, and in the past: OccurredOnUtc is the business fact's time
    // and a receiver that reached for it would order the inbox claim on a producer's clock.
    private static readonly DateTimeOffset OccurredOn =
        new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReceiveAsync_WhenContractNameIsUnbound_ThrowsPermanentlyAndWritesNothing()
    {
        var harness = Harness.Build(consumers: 0, bindContract: false);

        var ex = await Should.ThrowAsync<InboxPayloadException>(
            async () => await harness.Receiver.ReceiveAsync(Envelope()));

        ex.Message.ShouldContain(ContractName);
        ex.Message.ShouldContain("Consumes<TEvent>()");
        ex.Message.ShouldContain("dead-letter");

        // The assertion that matters is not that it threw — it is that nothing was recorded, so a
        // provider dead-lettering the message discards nothing this process was holding.
        harness.Writer.Written.ShouldBeEmpty();
        harness.Scopes.Contexts.ShouldBeEmpty("resolution fails before any scope is paid for");
    }

    [Fact]
    public async Task ReceiveAsync_WritesOneRowPerRegisteredConsumer()
    {
        var harness = Harness.Build(consumers: 2);

        var result = await harness.Receiver.ReceiveAsync(Envelope());

        result.RowsAdded.ShouldBe(2);
        result.Duplicates.ShouldBe(0);
        result.ConsumersMatched.ShouldBe(2);

        harness.Writer.Written.Count.ShouldBe(2);
        harness.Writer.Written.Select(m => m.ConsumerType)
            .ShouldBe([ConsumerA, ConsumerB], ignoreOrder: true);

        // Distinct rows, not one instance written twice: RowId is the surrogate primary key and a
        // shared one would collide on the second insert.
        harness.Writer.Written.Select(m => m.RowId).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public async Task ReceiveAsync_CopiesTheEnvelopeOntoEveryRow()
    {
        var harness = Harness.Build(consumers: 2);
        var envelope = Envelope();

        await harness.Receiver.ReceiveAsync(envelope);

        foreach (var row in harness.Writer.Written)
        {
            row.MessageId.Value.ShouldBe(
                envelope.MessageId, "the consumer deduplicates on the producing row's id");
            row.TenantId.ShouldBe(envelope.TenantId);
            row.Payload.ShouldBe(envelope.Payload, "the payload travels byte for byte");
            row.CorrelationId!.Value.ShouldBe(envelope.CorrelationId!.Value);

            // COPIED, not derived. The row records who caused the MESSAGE, which the producer
            // assigned. Deriving here would overwrite the producer's link with this message's own
            // id and lose the hop the envelope carried.
            row.CausationId!.Value.ShouldBe(envelope.CausationId!.Value);

            // The producer's business clock, carried whole. Since ADR-MSG-018 made
            // IIntegrationEvent a bare marker the payload need carry no timestamp of its own, so
            // dropping this would leave the business time nowhere on the receiving side.
            row.OccurredOnUtc.ShouldBe(envelope.OccurredOnUtc);

            row.Status.ShouldBe(InboxMessageStatus.Received);
            row.RetryCount.ShouldBe(0);
            row.DeadLettered.ShouldBeFalse();
            row.ProcessedAtUtc.ShouldBeNull();
            row.LockedUntilUtc.ShouldBeNull();
            row.ClaimToken.ShouldBeNull();
        }
    }

    [Fact]
    public async Task ReceiveAsync_StampsEventTypeFromTheResolvedLocalType_NotFromTheEnvelope()
    {
        var harness = Harness.Build(consumers: 1);

        await harness.Receiver.ReceiveAsync(Envelope());

        var eventType = harness.Writer.Written.ShouldHaveSingleItem().EventType;

        eventType.ShouldBe(typeof(InvoiceSettled).AssemblyQualifiedName);
        eventType.ShouldNotBe(ContractName, "a wire name resolves no CLR type");

        // Asserted through the REAL serializer rather than through Type.GetType, because that is
        // the path the drain takes: InboxProcessor hands this exact column to
        // IMessageSerializer.Deserialize. A column that merely looks right but does not round-trip
        // dead-letters every row on its first claim, with a payload that was perfectly well-formed.
        new SystemTextJsonMessageSerializer()
            .Deserialize(harness.Writer.Written[0].Payload, eventType)
            .ShouldBeOfType<InvoiceSettled>();
    }

    [Fact]
    public async Task ReceiveAsync_StampsReceivedAtUtcFromTheClock_NotFromOccurredOnUtc()
    {
        var harness = Harness.Build(consumers: 1);

        await harness.Receiver.ReceiveAsync(Envelope());

        var row = harness.Writer.Written.ShouldHaveSingleItem();

        // ReceivedAtUtc is the inbox claim's ordering key. Seeded six years apart so copying the
        // wrong one cannot pass by coincidence.
        row.ReceivedAtUtc.ShouldBe(ReceivedAt);
        row.ReceivedAtUtc.ShouldNotBe(OccurredOn);

        // And the other direction, which only matters now that BOTH clocks are recorded: a row
        // stamping the receipt instant into the business column reads as a fact that occurred on
        // arrival. Neither assertion alone catches a swap of the two.
        row.OccurredOnUtc.ShouldBe(OccurredOn);
        row.OccurredOnUtc.ShouldNotBe(ReceivedAt);
    }

    /// <summary>
    /// An envelope with no correlation gets ONE minted at the seam, shared by the whole fan-out.
    /// </summary>
    /// <remarks>
    /// This seam is the last point at which a single id still covers every consumer.
    /// <c>OutboxMessageFactory.ResolveCorrelation</c> substitutes a fresh id per staging call, so
    /// leaving null here would put N consumers of one delivery on N unrelated chains, none of them
    /// reaching back to the delivery that caused all of them. Two consumers, because the shared-ness
    /// is invisible with one.
    /// </remarks>
    [Fact]
    public async Task ReceiveAsync_WhenTheEnvelopeCarriesNoCorrelation_MintsOneForTheWholeFanOut()
    {
        var harness = Harness.Build(consumers: 2);
        var envelope = Envelope() with { CorrelationId = null };

        await harness.Receiver.ReceiveAsync(envelope);

        var correlations = harness.Writer.Written
            .Select(r => r.CorrelationId.ShouldNotBeNull().Value)
            .ToList();

        correlations.Count.ShouldBe(2);
        correlations.Distinct().Count().ShouldBe(
            1, "one chain for one delivery — a per-row id would correlate nothing");
        correlations[0].ShouldNotBe(Guid.Empty);

        // The scope carries the same one, so anything a handler stages downstream joins the chain
        // rather than starting its own.
        harness.Scopes.Contexts.ShouldHaveSingleItem()
            .CorrelationId.ShouldBe(correlations[0].ToString());
    }

    [Fact]
    public async Task ReceiveAsync_WhenTheEnvelopeCarriesACorrelation_CopiesItRatherThanMinting()
    {
        var harness = Harness.Build(consumers: 2);
        var envelope = Envelope();

        await harness.Receiver.ReceiveAsync(envelope);

        harness.Writer.Written.ShouldAllBe(r => r.CorrelationId!.Value == envelope.CorrelationId!.Value);
    }

    [Fact]
    public async Task ReceiveAsync_WhenOneConsumerIsADuplicate_StillWritesTheOthers()
    {
        // The property the retired InboxRedeliveryTests carried as its fourth, and the defect
        // ADR-MSG-017 §6 exists to prevent: an early return here made a partial redelivery
        // permanent loss for every consumer after the duplicated one.
        var harness = Harness.Build(
            consumers: 2,
            write: row => row.ConsumerType == ConsumerA
                ? InboxWriteResult.AlreadyPresent
                : InboxWriteResult.Added);

        var result = await harness.Receiver.ReceiveAsync(Envelope());

        result.RowsAdded.ShouldBe(1);
        result.Duplicates.ShouldBe(1);
        result.ConsumersMatched.ShouldBe(2);

        harness.Writer.Written.Select(m => m.ConsumerType)
            .ShouldContain(ConsumerB, "the duplicate must not cost the next consumer its row");
    }

    [Fact]
    public async Task ReceiveAsync_WhenEveryConsumerIsADuplicate_ReportsItAndDoesNotThrow()
    {
        var harness = Harness.Build(consumers: 2, write: _ => InboxWriteResult.AlreadyPresent);

        var result = await harness.Receiver.ReceiveAsync(Envelope());

        result.RowsAdded.ShouldBe(0);
        result.Duplicates.ShouldBe(2);

        // A redelivery is the nominal path under at-least-once delivery. Debug, never Warning:
        // one expired lease after a crash produces a burst of these.
        harness.Logs.Records
            .Where(r => r.EventId == 2101)
            .ShouldAllBe(r => r.Level == LogLevel.Debug);

        harness.Logs.Records.ShouldNotContain(r => r.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task ReceiveAsync_WhenNoHandlerIsRegistered_WritesNothingAndWarns()
    {
        // The composition a consuming service actually gets wrong: Consumes<T>() declared,
        // AddMessageHandler<,>() forgotten. Zero rows is correct — but a silent zero is
        // indistinguishable from a healthy delivery, which is the failure this module blocks on.
        var harness = Harness.Build(consumers: 0);

        var result = await harness.Receiver.ReceiveAsync(Envelope());

        result.ConsumersMatched.ShouldBe(0);
        result.RowsAdded.ShouldBe(0);
        harness.Writer.Written.ShouldBeEmpty();

        var warning = harness.Logs.Records.ShouldHaveSingleItem();
        warning.Level.ShouldBe(LogLevel.Warning);
        warning.EventId.ShouldBe(2102);
        warning.Message.ShouldContain(ContractName);
        warning.Message.ShouldContain(nameof(InvoiceSettled));
    }

    [Fact]
    public async Task ReceiveAsync_WhenNoHandlerIsRegistered_CountsIt()
    {
        var harness = Harness.Build(consumers: 0);

        var unconsumed = 0L;
        using var listener = new MeterListener();

        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == InboxMetrics.MeterName
                && instrument.Name == "microkit.inbox.envelopes.unconsumed")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>(
            (_, measurement, _, _) => Interlocked.Add(ref unconsumed, measurement));

        listener.Start();

        await harness.Receiver.ReceiveAsync(Envelope());

        // A log line carries an event; a counter carries a rate, and the rate is what an operator
        // alerts on. The plural of "one warning nobody read" is still zero.
        unconsumed.ShouldBe(1);
    }

    [Fact]
    public async Task ReceiveAsync_BuildsOneScopeCarryingTheTenantCorrelationAndDerivedCausation()
    {
        var harness = Harness.Build(consumers: 2);
        var envelope = Envelope();

        await harness.Receiver.ReceiveAsync(envelope);

        // ONE scope for the whole fan-out, not one per consumer: every row carries the same payload
        // and the same tenant, so there is nothing to isolate between them.
        var context = harness.Scopes.Contexts.ShouldHaveSingleItem();

        context.TenantId.ShouldBe(envelope.TenantId, "a tenant-aware factory resolves the database from this");
        context.CorrelationId.ShouldBe(envelope.CorrelationId!.Value.ToString(), "correlation is copied");

        // DERIVED from the envelope's own MessageId — everything recorded in this scope was caused
        // by delivering THIS message. Copying envelope.CausationId would name the grandparent, and
        // the envelope seeds the two differently so that copy-through cannot pass.
        context.CausationId.ShouldBe(envelope.MessageId.ToString());
        context.CausationId.ShouldNotBe(envelope.CausationId!.Value.ToString());
    }

    [Fact]
    public async Task ReceiveAsync_WhenTheInboxWriterIsNotRegistered_FailsAsAConfigurationFault()
    {
        // A host that composed contracts but never called AddEfCoreOutbox<TContext>(). Typed, so a
        // provider does not nack forever against a defect no redelivery can fix.
        var harness = Harness.Build(consumers: 1, registerWriter: false);

        var ex = await Should.ThrowAsync<InboxConfigurationException>(
            async () => await harness.Receiver.ReceiveAsync(Envelope()));

        ex.Message.ShouldContain(nameof(IInboxWriter));
        ex.Message.ShouldContain("AddEfCoreOutbox");
    }

    /// <summary>
    /// A writer that is registered but cannot be activated is NOT a configuration fault.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The distinction this pins is the whole of the fix. <c>IInboxWriter</c> resolves through a
    /// factory that activates <c>EfInboxStore&lt;TContext&gt;</c> and, with it, the consumer's
    /// <c>DbContext</c> — so a <c>catch (InvalidOperationException)</c> around the resolution
    /// spans that entire graph while reading as a registration lookup. Under a tenant-aware
    /// <see cref="IExecutionScopeFactory"/> an envelope naming an unknown tenant throws exactly
    /// that from the connection resolution, and catching it would report a present registration
    /// as missing and stop the consume loop for every tenant over one message's data.
    /// </para>
    /// <para>
    /// The exception type below is <see cref="InvalidOperationException"/> on purpose: it is the
    /// one a catch-based implementation would swallow, so any other type here would leave the
    /// regression free to come back. Asserting <c>ShouldNotBeOfType</c> as well as the identity
    /// says which half failed when it does.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ReceiveAsync_WhenTheWriterCannotBeActivated_PropagatesRatherThanClassifying()
    {
        var activationFailure = new InvalidOperationException(
            "No connection string is configured for tenant 'tenant-a'.");

        var harness = Harness.Build(consumers: 1, writerActivationFailure: activationFailure);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            async () => await harness.Receiver.ReceiveAsync(Envelope()));

        ex.ShouldNotBeOfType<InboxConfigurationException>(
            "a registered writer that fails to activate is one message's fault — most likely a " +
            "tenant whose connection cannot be resolved — and must not stop the consume loop");

        ex.ShouldBeSameAs(activationFailure, "it propagates unchanged, so a provider can nack");
    }

    [Fact]
    public async Task ReceiveAsync_WithNoEnvelope_Throws()
    {
        var harness = Harness.Build(consumers: 1);

        await Should.ThrowAsync<ArgumentNullException>(
            async () => await harness.Receiver.ReceiveAsync(null!));
    }

    private static MessageEnvelope Envelope()
        => new(
            MessageId: Guid.Parse("11111111-1111-4111-8111-111111111111"),
            ContractName: ContractName,
            Source: Source,
            Payload: """{"invoiceId":"22222222-2222-4222-8222-222222222222"}""",
            TenantId: "tenant-a",
            CorrelationId: Guid.Parse("33333333-3333-4333-8333-333333333333"),

            // Different from MessageId on purpose: the row must COPY this one while the execution
            // context DERIVES from MessageId. A shared value would let either bug pass.
            CausationId: Guid.Parse("44444444-4444-4444-8444-444444444444"),
            OccurredOnUtc: OccurredOn);

    private sealed record Harness(
        EnvelopeReceiver Receiver,
        RecordingInboxWriter Writer,
        TestExecutionScopeFactory Scopes,
        CapturingLogger<EnvelopeReceiver> Logs)
    {
        internal static Harness Build(
            int consumers,
            bool bindContract = true,
            bool registerWriter = true,
            Func<InboxMessage, InboxWriteResult>? write = null,
            Exception? writerActivationFailure = null)
        {
            var writer = new RecordingInboxWriter(write);

            var services = new ServiceCollection();
            if (writerActivationFailure is not null)
            {
                // REGISTERED, and unactivatable — the shape a tenant-aware scope produces when it
                // cannot resolve a connection for the envelope's tenant. Distinct from
                // registerWriter: false, which is the absent-registration case.
                services.AddScoped<IInboxWriter>(_ => throw writerActivationFailure);
            }
            else if (registerWriter)
            {
                services.AddScoped<IInboxWriter>(_ => writer);
            }

            var provider = services.BuildServiceProvider();
            var scopes = new TestExecutionScopeFactory(
                provider.GetRequiredService<IServiceScopeFactory>());

            var contracts = bindContract
                ? IntegrationEventContractFixtures.ConsumingRegistry(e => e.Consumes<InvoiceSettled>())
                : IntegrationEventContractFixtures.ConsumingRegistry();

            var handlers = new MessageHandlerRegistry();
            if (consumers >= 1)
            {
                handlers.RegisterGeneric<InvoiceSettled>(ConsumerA, typeof(FirstConsumer));
            }

            if (consumers >= 2)
            {
                handlers.RegisterGeneric<InvoiceSettled>(ConsumerB, typeof(SecondConsumer));
            }

            var logs = new CapturingLogger<EnvelopeReceiver>();

            var receiver = new EnvelopeReceiver(
                contracts,
                handlers,
                scopes,
                new FakeTimeProvider(ReceivedAt),
                new InboxMetrics(),
                logs);

            return new Harness(receiver, writer, scopes, logs);
        }
    }

    private sealed class FirstConsumer : IMessageHandler<InvoiceSettled>
    {
        public ValueTask HandleAsync(InvoiceSettled evt, CancellationToken ct = default)
            => ValueTask.CompletedTask;
    }

    private sealed class SecondConsumer : IMessageHandler<InvoiceSettled>
    {
        public ValueTask HandleAsync(InvoiceSettled evt, CancellationToken ct = default)
            => ValueTask.CompletedTask;
    }
}
