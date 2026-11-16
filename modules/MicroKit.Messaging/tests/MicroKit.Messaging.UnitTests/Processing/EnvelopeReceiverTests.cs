namespace MicroKit.Messaging.UnitTests.Processing;

using System.Diagnostics.Metrics;

using MicroKit.Messaging.Publishing;
using MicroKit.Messaging.UnitTests.Publishing;

/// <summary>
/// <see cref="EnvelopeReceiver"/> — the receiving seam (#188): one envelope through the receive
/// pipeline, each handler guarded by its inbox row when it has a store, and a disposition for the
/// transport.
/// </summary>
/// <remarks>
/// The row columns worth their own tests are the ones the envelope does <b>not</b> carry:
/// <c>EventType</c>, which must name a type <i>this</i> process can load; <c>ReceivedAtUtc</c>, which
/// must come from the clock and not from a caller-supplied business timestamp; and
/// <c>ConsumerType</c>, which comes from the handler registry.
/// </remarks>
public sealed class EnvelopeReceiverTests
{
    private const string ContractName = "partner.billing.invoice-settled.v1";
    private const string Source = "/partner/billing";
    private static readonly string ConsumerA = typeof(FirstConsumer).AssemblyQualifiedName!;
    private static readonly string ConsumerB = typeof(SecondConsumer).AssemblyQualifiedName!;

    // Deliberately far from the clock, and in the past: OccurredOnUtc is the business fact's time
    // and a receiver that reached for it would order the retry claim on a producer's clock.
    private static readonly DateTimeOffset OccurredOn = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReceiveAsync_WhenContractNameIsUnbound_DeadLettersAndRunsNothing()
    {
        var harness = Harness.Build(consumers: 0, bindContract: false);

        var result = await harness.Receiver.ReceiveAsync(Envelope());

        result.Disposition.ShouldBe(ReceiveDisposition.DeadLetter);
        result.Reason!.ShouldContain(ContractName);
        harness.Guard.Database.Rows.ShouldBeEmpty();
        harness.Guard.Scopes.Contexts.ShouldBeEmpty("resolution fails before any scope is paid for");
    }

    [Fact]
    public async Task ReceiveAsync_WritesOneProcessedRowPerHandler_AndAcknowledges()
    {
        var harness = Harness.Build(consumers: 2);

        var result = await harness.Receiver.ReceiveAsync(Envelope());

        result.Disposition.ShouldBe(ReceiveDisposition.Acknowledge);
        harness.Probe.Calls(ConsumerA).ShouldBe(1);
        harness.Probe.Calls(ConsumerB).ShouldBe(1);
        harness.Guard.Database.Rows.Values.Select(r => r.ConsumerType).ShouldBe([ConsumerA, ConsumerB], ignoreOrder: true);
        harness.Guard.Database.Rows.Values.ShouldAllBe(r => r.Status == InboxMessageStatus.Processed);
    }

    [Fact]
    public async Task ReceiveAsync_CopiesTheEnvelopeOntoTheRow()
    {
        var harness = Harness.Build(consumers: 1);

        await harness.Receiver.ReceiveAsync(Envelope());

        var row = harness.Guard.Database.Rows.Values.ShouldHaveSingleItem();
        row.MessageId.ShouldBe(MessageId.From(Guid.Parse(EnvelopeId)));
        row.TenantId.ShouldBe(Tenant);
        row.CorrelationId.ShouldBe(CorrelationId.From(Guid.Parse(Correlation)));
        row.CausationId.ShouldBe(CausationId.From(Guid.Parse(Causation)), "the row COPIES the producer's causation");
        row.OccurredOnUtc.ShouldBe(OccurredOn);
        row.Source.ShouldBe(Source);
        row.Headers![MessageHeaders.TenantId].ShouldBe(Tenant);
        row.Payload.ShouldBe(Payload);
    }

    [Fact]
    public async Task ReceiveAsync_StampsEventTypeFromTheResolvedLocalType_NotFromTheEnvelope()
    {
        var harness = Harness.Build(consumers: 1);

        await harness.Receiver.ReceiveAsync(Envelope());

        var row = harness.Guard.Database.Rows.Values.ShouldHaveSingleItem();
        row.EventType.ShouldBe(typeof(InvoiceSettled).AssemblyQualifiedName);

        // Through the real serializer, the path a retry takes.
        new MicroKit.Messaging.Serialization.SystemTextJsonMessageSerializer()
            .Deserialize(System.Text.Encoding.UTF8.GetBytes(row.Payload), typeof(InvoiceSettled))
            .ShouldBeOfType<InvoiceSettled>();
    }

    [Fact]
    public async Task ReceiveAsync_StampsReceivedAtUtcFromTheClock_NotFromOccurredOnUtc()
    {
        var harness = Harness.Build(consumers: 1);

        await harness.Receiver.ReceiveAsync(Envelope());

        var row = harness.Guard.Database.Rows.Values.ShouldHaveSingleItem();
        row.ReceivedAtUtc.ShouldBe(GuardHarness.Now);
        row.OccurredOnUtc.ShouldBe(OccurredOn);
    }

    [Fact]
    public async Task ReceiveAsync_WhenTheEnvelopeCarriesNoCorrelation_MintsOneForEveryHandler()
    {
        var harness = Harness.Build(consumers: 2);

        await harness.Receiver.ReceiveAsync(Envelope(withCorrelation: false));

        var correlations = harness.Guard.Database.Rows.Values.Select(r => r.CorrelationId).Distinct().ToList();
        correlations.ShouldHaveSingleItem().ShouldNotBeNull("one id, minted once, for the whole fan-out");
        harness.Probe.Seen.Select(s => s.Context.CorrelationId).Distinct().ShouldHaveSingleItem()
            .ShouldBe(correlations[0]!.Value.ToString());
    }

    [Fact]
    public async Task ReceiveAsync_WhenTheEnvelopeCarriesACorrelation_CopiesItRatherThanMinting()
    {
        var harness = Harness.Build(consumers: 1);

        await harness.Receiver.ReceiveAsync(Envelope());

        harness.Probe.Seen.ShouldHaveSingleItem().Context.CorrelationId.ShouldBe(Correlation);
    }

    [Fact]
    public async Task ReceiveAsync_WhenOneHandlersRowExists_SkipsItAndStillRunsTheOthers()
    {
        var harness = Harness.Build(consumers: 2);
        harness.Guard.Database.Add(Row(ConsumerA, InboxMessageStatus.Processed));

        var result = await harness.Receiver.ReceiveAsync(Envelope());

        result.Disposition.ShouldBe(ReceiveDisposition.Acknowledge);
        harness.Probe.Calls(ConsumerA).ShouldBe(0, "its row says it already ran");
        harness.Probe.Calls(ConsumerB).ShouldBe(1, "a skipped handler must not cost the next one its run");
        harness.Guard.Logs.Records.ShouldContain(r => r.EventId == 2200 && r.Level == LogLevel.Debug);
    }

    [Theory]
    [InlineData(InboxMessageStatus.Failed)]
    [InlineData(InboxMessageStatus.Processing)]
    [InlineData(InboxMessageStatus.Received)]
    public async Task ReceiveAsync_WhenTheRowExistsInAnyState_NeverStartsASecondAttempt(InboxMessageStatus status)
    {
        var harness = Harness.Build(consumers: 1);
        harness.Guard.Database.Add(Row(ConsumerA, status));

        var result = await harness.Receiver.ReceiveAsync(Envelope());

        result.Disposition.ShouldBe(ReceiveDisposition.Acknowledge);
        harness.Probe.Calls(ConsumerA).ShouldBe(0, "the store's retry loop owns the row");
    }

    [Fact]
    public async Task ReceiveAsync_WhenAHandlerFails_WritesItsRowFailed_RunsTheOthers_AndAcknowledges()
    {
        var harness = Harness.Build(consumers: 2);
        harness.Probe.Throw[ConsumerA] = new InvalidOperationException("the handler failed");

        var result = await harness.Receiver.ReceiveAsync(Envelope());

        result.Disposition.ShouldBe(ReceiveDisposition.Acknowledge, "the Failed row is committed: the store retries it");
        var failed = harness.Guard.Database.Find(new InboxMessageKey(MessageId.From(Guid.Parse(EnvelopeId)), ConsumerA))!;
        failed.Status.ShouldBe(InboxMessageStatus.Failed);
        failed.RetryCount.ShouldBe(1);
        failed.DeadLettered.ShouldBeFalse();
        failed.NextRetryAtUtc.ShouldBe(GuardHarness.Now + TimeSpan.FromSeconds(2), "2^1 s, no jitter");
        failed.ErrorMessage.ShouldBe("the handler failed");
        harness.Probe.Calls(ConsumerB).ShouldBe(1);
    }

    [Fact]
    public async Task ReceiveAsync_WhenTheFailedRowCannotBeWritten_AsksForARedelivery()
    {
        var harness = Harness.Build(consumers: 1);
        harness.Probe.Throw[ConsumerA] = new InvalidOperationException("the handler failed");
        harness.Guard.Database.FailInsertFailed = new TimeoutException("the database is gone");

        var result = await harness.Receiver.ReceiveAsync(Envelope());

        result.Disposition.ShouldBe(ReceiveDisposition.Redeliver, "nothing records the failure, so the transport must keep the message");
        harness.Guard.Database.Rows.ShouldBeEmpty();
        harness.Guard.Logs.Records.ShouldContain(r => r.EventId == 2204 && r.Level == LogLevel.Error);
    }

    [Fact]
    public async Task ReceiveAsync_WhenAHandlerThrowsPermanently_DeadLettersItsRow_AndAcknowledges()
    {
        var harness = Harness.Build(consumers: 1);
        harness.Probe.Throw[ConsumerA] = new PermanentMessageException("never deliverable");

        var result = await harness.Receiver.ReceiveAsync(Envelope());

        result.Disposition.ShouldBe(ReceiveDisposition.Acknowledge);
        var row = harness.Guard.Database.Rows.Values.ShouldHaveSingleItem();
        row.Status.ShouldBe(InboxMessageStatus.Failed);
        row.DeadLettered.ShouldBeTrue();
        row.NextRetryAtUtc.ShouldBeNull();
    }

    [Fact]
    public async Task ReceiveAsync_WhenADependencyIsDown_WritesNoRow_AndAsksToPauseAndRedeliver()
    {
        var harness = Harness.Build(consumers: 1);
        harness.Probe.Throw[ConsumerA] = new InboxDependencyUnavailableException("the partner API is down");

        var result = await harness.Receiver.ReceiveAsync(Envelope());

        result.Disposition.ShouldBe(ReceiveDisposition.PauseAndRedeliver);
        harness.Guard.Database.Rows.ShouldBeEmpty("an outage counts no attempt");
    }

    [Fact]
    public async Task ReceiveAsync_WithNoStore_AHandlerFailureAsksForARedelivery()
    {
        var harness = Harness.Build(consumers: 1, withStore: false);
        harness.Probe.Throw[ConsumerA] = new InvalidOperationException("the handler failed");

        var result = await harness.Receiver.ReceiveAsync(Envelope());

        result.Disposition.ShouldBe(ReceiveDisposition.Redeliver, "without a store the transport owns the retries (#186)");
        result.Error.ShouldBeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task ReceiveAsync_WhenTheHostIsStopping_LeavesTheMessageUnsettled()
    {
        var harness = Harness.Build(consumers: 1);
        using var stopping = new CancellationTokenSource();
        harness.Probe.Throw[ConsumerA] = new OperationCanceledException(stopping.Token);
        await stopping.CancelAsync();

        var result = await harness.Receiver.ReceiveAsync(Envelope(), stopping.Token);

        result.Disposition.ShouldBe(ReceiveDisposition.Unsettled);
        harness.Guard.Database.Rows.ShouldBeEmpty("a stop is never a failure");
    }

    [Fact]
    public async Task ReceiveAsync_WhenNoHandlerIsRegistered_AcknowledgesAndWarns()
    {
        var harness = Harness.Build(consumers: 0);

        var result = await harness.Receiver.ReceiveAsync(Envelope());

        result.Disposition.ShouldBe(ReceiveDisposition.Acknowledge);
        harness.Guard.Database.Rows.ShouldBeEmpty();
        var warning = harness.Guard.Logs.Records.ShouldHaveSingleItem();
        warning.EventId.ShouldBe(3001);
        warning.Level.ShouldBe(LogLevel.Warning);
    }

    [Fact]
    public async Task ReceiveAsync_WhenNoHandlerIsRegistered_CountsIt()
    {
        // A log carries an event; a counter carries the rate.
        var harness = Harness.Build(consumers: 0);
        var counted = new List<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == MessagingDiagnostics.InboxMeterName
                && instrument.Name == "microkit.inbox.envelopes.unconsumed")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "contract.name")
                {
                    counted.Add((string)tag.Value!);
                }
            }
        });
        listener.Start();

        await harness.Receiver.ReceiveAsync(Envelope());

        counted.ShouldContain(ContractName);
    }

    [Fact]
    public async Task ReceiveAsync_BuildsEachScopeWithTheTenantCorrelationAndDerivedCausation()
    {
        var harness = Harness.Build(consumers: 1);

        await harness.Receiver.ReceiveAsync(Envelope());

        harness.Guard.Scopes.Contexts.ShouldNotBeEmpty();
        harness.Guard.Scopes.Contexts.ShouldAllBe(c =>
            c.TenantId == Tenant
            && c.CorrelationId == Correlation
            // DERIVED: the envelope's id, never its own causation — that names the grandparent.
            && c.CausationId == EnvelopeId);
    }

    [Fact]
    public async Task ReceiveAsync_WhenTheStoreRegistersNoGuard_AsksForARedelivery()
    {
        var harness = Harness.Build(consumers: 1, registerGuard: false);

        var result = await harness.Receiver.ReceiveAsync(Envelope());

        result.Disposition.ShouldBe(ReceiveDisposition.Redeliver);
        result.Error.ShouldBeOfType<InboxConfigurationException>();
        harness.Probe.Calls(ConsumerA).ShouldBe(0);
        harness.Guard.Logs.Records.ShouldContain(r => r.EventId == 2207);
    }

    [Fact]
    public async Task ReceiveAsync_WhenTheGuardCannotBeActivated_AsksForARedelivery_RatherThanAConfigurationVerdict()
    {
        // Registered but unactivatable is one tenant's connection failing, not a composition fault.
        var harness = Harness.Build(
            consumers: 1, guardActivationFailure: new InvalidOperationException("no connection for tenant-a"));

        var result = await harness.Receiver.ReceiveAsync(Envelope());

        result.Disposition.ShouldBe(ReceiveDisposition.Redeliver);
        result.Error.ShouldBeOfType<InvalidOperationException>();
        harness.Probe.Calls(ConsumerA).ShouldBe(0);
    }

    [Fact]
    public async Task ReceiveAsync_WhenTheBodyIsNotJson_DeadLettersAndRunsNothing()
    {
        var harness = Harness.Build(consumers: 1);

        var result = await harness.Receiver.ReceiveAsync(Envelope(contentType: "application/avro"));

        result.Disposition.ShouldBe(ReceiveDisposition.DeadLetter);
        result.Reason!.ShouldContain("application/avro");
        harness.Probe.Calls(ConsumerA).ShouldBe(0);
    }

    [Fact]
    public async Task ReceiveAsync_AcceptsAJsonContentTypeWithParameters()
    {
        var harness = Harness.Build(consumers: 1);

        var result = await harness.Receiver.ReceiveAsync(Envelope(contentType: "application/json; charset=utf-8"));

        result.Disposition.ShouldBe(ReceiveDisposition.Acknowledge);
        harness.Probe.Calls(ConsumerA).ShouldBe(1);
    }

    [Fact]
    public async Task ReceiveAsync_WhenTheIdIsNotAGuid_DeadLettersAndRunsNothing()
    {
        var harness = Harness.Build(consumers: 1);

        var result = await harness.Receiver.ReceiveAsync(Envelope(id: "not-a-guid"));

        result.Disposition.ShouldBe(ReceiveDisposition.DeadLetter);
        harness.Probe.Calls(ConsumerA).ShouldBe(0);
        harness.Guard.Database.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReceiveAsync_WithNoEnvelope_Throws()
    {
        var harness = Harness.Build(consumers: 1);

        await Should.ThrowAsync<ArgumentNullException>(
            async () => await harness.Receiver.ReceiveAsync(null!));
    }

    /// <summary>
    /// An initializer configures the scope the store's <c>DbContext</c> is built from — a tenant's
    /// connection, say — so it runs before the guard is built, in every scope the guard uses: the
    /// read, each attempt, and the write of a Failed row.
    /// </summary>
    [Fact]
    public async Task ReceiveAsync_RunsTheScopeInitializersBeforeTheStoreIsBuilt_InEveryScope()
    {
        var seen = new List<string?>();
        var harness = Harness.Build(
            consumers: 1,
            services: s =>
            {
                s.AddScoped<TenantSlot>();
                s.AddSingleton<IMessageScopeInitializer, TenantSlotInitializer>();
            },
            onGuardBuilt: sp =>
            {
                lock (seen)
                {
                    seen.Add(sp.GetRequiredService<TenantSlot>().Tenant);
                }
            });
        harness.Probe.Throw[ConsumerA] = new InvalidOperationException("fails, so the Failed row's scope is used too");

        await harness.Receiver.ReceiveAsync(Envelope());

        seen.Count.ShouldBeGreaterThanOrEqualTo(3, "the read, the attempt and the Failed row");
        seen.ShouldAllBe(t => t == Tenant, "every guard was built after the initializer filled its scope");
    }

    [Fact]
    public async Task ReceiveAsync_WhenADependencyIsDownAndTheDatabaseToo_StillAsksToPause()
    {
        // The duplicate read after the failure would fail as well: it must not hide the verdict.
        var harness = Harness.Build(consumers: 1);
        harness.Probe.Throw[ConsumerA] = new InboxDependencyUnavailableException("down");
        harness.Probe.OnHit = () => harness.Guard.Database.FailReads = true;

        var result = await harness.Receiver.ReceiveAsync(Envelope());

        result.Disposition.ShouldBe(ReceiveDisposition.PauseAndRedeliver);
    }

    [Fact]
    public async Task ReceiveAsync_WhenItsOwnCommitLandedAndThenFaulted_IsNotCountedAsAConcurrentDuplicate()
    {
        // No retrying strategy, so nothing verifies the commit: the engine recognises its own row.
        var harness = Harness.Build(consumers: 1);
        harness.Guard.Database.FailAfterNextCommit = new TimeoutException("the connection dropped after COMMIT");

        var result = await harness.Receiver.ReceiveAsync(Envelope());

        result.Disposition.ShouldBe(ReceiveDisposition.Acknowledge);
        harness.Guard.Logs.Records.ShouldContain(r => r.EventId == 2206);
        harness.Guard.Logs.Records.ShouldNotContain(r => r.EventId == 2201, "no other delivery was involved");
        harness.Guard.Database.Rows.Values.ShouldHaveSingleItem().Status.ShouldBe(InboxMessageStatus.Processed);
    }

    private const string EnvelopeId = "11111111-1111-4111-8111-111111111111";
    private const string Payload = """{"invoiceId":"22222222-2222-4222-8222-222222222222"}""";
    private const string Tenant = "tenant-a";
    private const string Correlation = "33333333-3333-4333-8333-333333333333";

    // Different from the id on purpose: the row must COPY this one while the execution context
    // DERIVES from the id. A shared value would let either bug pass.
    private const string Causation = "44444444-4444-4444-8444-444444444444";

    private static InboxMessage Row(string consumer, InboxMessageStatus status) => new()
    {
        MessageId = MessageId.From(Guid.Parse(EnvelopeId)),
        ConsumerType = consumer,
        EventType = typeof(InvoiceSettled).AssemblyQualifiedName!,
        Payload = Payload,
        Status = status,
    };

    private static MessageEnvelope Envelope(
        bool withCorrelation = true,
        string id = EnvelopeId,
        string contentType = "application/json")
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [MessageHeaders.TenantId] = Tenant,
            [MessageHeaders.CausationId] = Causation,
        };

        if (withCorrelation)
        {
            headers[MessageHeaders.CorrelationId] = Correlation;
        }

        return new MessageEnvelope
        {
            Id = id,
            Type = ContractName,
            Source = Source,
            Time = OccurredOn,
            DataContentType = contentType,
            Headers = headers,
            Body = System.Text.Encoding.UTF8.GetBytes(Payload),
        };
    }

    private sealed record Harness(EnvelopeReceiver Receiver, GuardHarness Guard, HandlerProbe Probe)
    {
        internal static Harness Build(
            int consumers,
            bool bindContract = true,
            bool withStore = true,
            bool registerGuard = true,
            Exception? guardActivationFailure = null,
            Action<IServiceCollection>? services = null,
            Action<IServiceProvider>? onGuardBuilt = null)
        {
            var probe = new HandlerProbe();

            var contracts = bindContract
                ? IntegrationEventContractFixtures.Registry(bus => bus.AddHandler<InvoiceSettledHandler>())
                : IntegrationEventContractFixtures.Registry(_ => { });

            var handlers = new MessageHandlerRegistry();
            if (consumers >= 1)
            {
                handlers.RegisterGeneric<InvoiceSettled>(ConsumerA, typeof(FirstConsumer));
            }

            if (consumers >= 2)
            {
                handlers.RegisterGeneric<InvoiceSettled>(ConsumerB, typeof(SecondConsumer));
            }

            var guard = GuardHarness.Build(
                collection =>
                {
                    collection.AddSingleton(probe);
                    collection.AddScoped<FirstConsumer>();
                    collection.AddScoped<SecondConsumer>();
                    services?.Invoke(collection);
                },
                handlers,
                contracts,
                withStore,
                registerGuard,
                guardActivationFailure,
                onGuardBuilt: onGuardBuilt);

            return new Harness(new EnvelopeReceiver(guard.Pipeline), guard, probe);
        }
    }

    private sealed class TenantSlot
    {
        public string? Tenant { get; set; }
    }

    private sealed class TenantSlotInitializer : IMessageScopeInitializer
    {
        public ValueTask InitializeAsync(MessageContext context, IServiceProvider services, CancellationToken ct = default)
        {
            services.GetRequiredService<TenantSlot>().Tenant = context.TenantId;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FirstConsumer(HandlerProbe probe, IServiceProvider services) : IMessageHandler<InvoiceSettled>
    {
        public ValueTask HandleAsync(InvoiceSettled message, MessageContext context, CancellationToken ct = default)
        {
            probe.Hit(ConsumerA, context, services);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SecondConsumer(HandlerProbe probe, IServiceProvider services) : IMessageHandler<InvoiceSettled>
    {
        public ValueTask HandleAsync(InvoiceSettled message, MessageContext context, CancellationToken ct = default)
        {
            probe.Hit(ConsumerB, context, services);
            return ValueTask.CompletedTask;
        }
    }
}
