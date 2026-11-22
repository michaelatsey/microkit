// NSubstitute ValueTask setup chains (.Returns on the method-call result) are intentional.
#pragma warning disable CA2012
using Microsoft.Extensions.Time.Testing;
using MicroKit.Messaging.Stores;

namespace MicroKit.Messaging.UnitTests.Processing;

/// <summary>
/// The outbox's instruments (#126): what a batch settled, counted at least once — whenever its settlement
/// wrote rows; the dispatch delay; and the back-off up-down counters, which count an endpoint or a tenant
/// from its down verdict until a success, or until a ceiling plus MaxPollingInterval after its back-off
/// ended with no new attempt. Each test listens to its own <see cref="OutboxMetrics"/>'s meter.
/// </summary>
public sealed class OutboxMetricsTests
{
    private const string Settled = "microkit.outbox.messages.settled";
    private const string Delay = "microkit.outbox.dispatch.delay";
    private const string Endpoints = "microkit.outbox.endpoints.backoff";
    private const string Tenants = "microkit.outbox.tenants.backoff";

    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly MessageStoreKey Store = new("tests:outbox-metrics");
    private static readonly OutboxProcessorOptions Options = new();

    /// <summary>How long past its end a back-off still counts: the ceiling plus the loop's longest idle delay.</summary>
    private static readonly TimeSpan HalfOpen = Options.TransportUnavailableBackoff + Options.MaxPollingInterval;

    private static OutboxMessage Row(string endpoint, string? tenant)
    {
        var row = endpoint == "transport"
            ? OutboxFixtures.ContractMessage(tenantId: tenant)
            : OutboxFixtures.Message(tenantId: tenant);
        if (endpoint == "local")
        {
            row.Endpoint = OutboxMessage.LocalEndpoint;
        }

        row.CreatedAtUtc = Now;
        return row;
    }

    // ---------------------------------------------------------------------------
    // V1, V2 — microkit.outbox.messages.settled
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ASettledBatch_CountsEachEndpointsOutcomes_AndNothingForItsReleasedRows()
    {
        using var rig = new Rig();
        using var stop = new CancellationTokenSource();
        var faults = new Dictionary<MessageId, Func<Exception>>();
        var rows = new List<OutboxMessage>();
        foreach (var endpoint in new[] { "transport", "local" })
        {
            var published = Row(endpoint, "tenant-a");
            var transient = Row(endpoint, "tenant-a");
            var poison = Row(endpoint, "tenant-a");
            var down = Row(endpoint, "tenant-b");
            var held = Row(endpoint, "tenant-b");
            faults[transient.Id] = () => new InvalidOperationException("a broker nack");
            faults[poison.Id] = () => new OutboxPayloadException("unreadable");
            faults[down.Id] = () => new OutboxTransportUnavailableException("tenant-b's credentials revoked");
            rows.AddRange([published, transient, poison, down, held]);
        }

        // The host stops during this row's dispatch: it and the row after it are released.
        var stopping = Row("transport", "tenant-a");
        faults[stopping.Id] = () =>
        {
            stop.Cancel();
            return new OperationCanceledException(stop.Token);
        };
        rows.AddRange([stopping, Row("local", "tenant-a")]);
        rig.Script = m => faults.TryGetValue(m.Id, out var fault) ? fault() : null;
        rig.Claims.Enqueue(OutboxFixtures.Claim([.. rows]));

        await rig.Processor.ProcessBatchAsync(Options.BatchSize, stop.Token);

        rig.Settled.Count(o => o.Kind == OutboxOutcomeKind.Released)
            .ShouldBe(2, "control: the batch did release two rows");
        var counted = rig.Recorder.Of(Settled);
        counted.ShouldAllBe(r => r.Tag("store.key") == Store.Value);
        Cells(counted).ShouldBe(
            [
                "local/dead_letter=1", "local/deferred=2", "local/published=1", "local/retry=1",
                "transport/dead_letter=1", "transport/deferred=2", "transport/published=1", "transport/retry=1",
            ],
            "one measurement per (endpoint, outcome), the deferred rows of a tenant in back-off included");
    }

    [Fact]
    public async Task ASettlementThatThrows_RecordsNoSettledRow()
    {
        using var rig = new Rig { Settle = _ => throw new InvalidOperationException("the store is down") };
        rig.Claims.Enqueue(OutboxFixtures.Claim(Row("transport", "tenant-a"), Row("local", "tenant-a")));

        await rig.Processor.ProcessBatchAsync(Options.BatchSize);

        rig.Recorder.Of(Delay).Count.ShouldBe(2, "control: both rows were handed over and sampled");
        rig.Recorder.Of(Settled).ShouldBeEmpty("its rows come back once their lease expires, and are counted then");
    }

    [Fact]
    public async Task ASettlementThatWroteNothing_RecordsNoSettledRow()
    {
        using var rig = new Rig { Settle = _ => 0 };
        rig.Claims.Enqueue(OutboxFixtures.Claim(Row("transport", "tenant-a"), Row("local", "tenant-a")));

        await rig.Processor.ProcessBatchAsync(Options.BatchSize);

        rig.Recorder.Of(Delay).Count.ShouldBe(2, "control: both rows were handed over and sampled");
        rig.Recorder.Of(Settled).ShouldBeEmpty("every lease was lost: the rows are another processor's to count");
    }

    [Fact]
    public async Task APartialSettlement_CountsItsWholeBatch_AtLeastOnce()
    {
        // The rows it wrote never come back; the row whose lease was lost is counted again by whoever
        // settles it — at least once, as the dispatch delay is.
        using var rig = new Rig { Settle = outcomes => outcomes.Count - 1 };
        var poison = Row("local", "tenant-a");
        rig.Script = m => m.Id == poison.Id ? new OutboxPayloadException("unreadable") : null;
        rig.Claims.Enqueue(OutboxFixtures.Claim(Row("transport", "tenant-a"), Row("transport", "tenant-a"), poison));

        await rig.Processor.ProcessBatchAsync(Options.BatchSize);

        Cells(rig.Recorder.Of(Settled)).ShouldBe(["local/dead_letter=1", "transport/published=2"]);
    }

    [Fact]
    public async Task AListenerThatThrows_NeverChangesARowsVerdict_NorTheBatchsFlow()
    {
        // A faulty exporter's callback runs on the drain loop's thread: a row the endpoint took is still
        // Published — never retried, charged and re-sent — and the settled batch still returns.
        using var rig = new Rig();
        using var faulty = new System.Diagnostics.Metrics.MeterListener();
        faulty.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter == rig.Metrics.Meter)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        faulty.SetMeasurementEventCallback<double>((_, _, _, _) => throw new InvalidOperationException("a faulty exporter"));
        faulty.SetMeasurementEventCallback<long>((_, _, _, _) => throw new InvalidOperationException("a faulty exporter"));
        faulty.Start();
        rig.Claims.Enqueue(OutboxFixtures.Claim(Row("transport", "tenant-a")));

        var result = await rig.Processor.ProcessBatchAsync(Options.BatchSize);

        rig.Settled.ShouldHaveSingleItem().Kind.ShouldBe(OutboxOutcomeKind.Published);
        result.Published.ShouldBe(1);
        result.Retried.ShouldBe(0);
    }

    // ---------------------------------------------------------------------------
    // V3 — microkit.outbox.dispatch.delay
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ADispatchedRow_RecordsItsDelayFromItsStaging_OnTheProcessorsClock()
    {
        using var rig = new Rig();
        var transport = Row("transport", "tenant-a");
        transport.CreatedAtUtc = Now - TimeSpan.FromSeconds(7);
        var local = Row("local", "tenant-a");
        local.CreatedAtUtc = Now - TimeSpan.FromSeconds(2.5);
        rig.Claims.Enqueue(OutboxFixtures.Claim(transport, local));

        await rig.Processor.ProcessBatchAsync(Options.BatchSize);

        var delays = rig.Recorder.Of(Delay);
        delays.Count.ShouldBe(2);
        delays[0].Value.ShouldBe(7.0);
        delays[0].Tag("endpoint").ShouldBe("transport");
        delays[0].Tag("store.key").ShouldBe(Store.Value);
        delays[1].Value.ShouldBe(2.5);
        delays[1].Tag("endpoint").ShouldBe("local");
    }

    [Fact]
    public async Task ARowStampedAheadOfTheProcessorsClock_RecordsAZeroDelay()
    {
        // Another instance's clock stamped the row: a negative delay is a skew, not a measure.
        using var rig = new Rig();
        var row = Row("transport", "tenant-a");
        row.CreatedAtUtc = Now + TimeSpan.FromSeconds(3);
        rig.Claims.Enqueue(OutboxFixtures.Claim(row));

        await rig.Processor.ProcessBatchAsync(Options.BatchSize);

        rig.Recorder.Of(Delay).ShouldHaveSingleItem().Value.ShouldBe(0d);
    }

    [Fact]
    public async Task AProcessorWithNoStoreKey_TagsNoStore()
    {
        using var rig = new Rig(withStore: false);
        rig.Claims.Enqueue(OutboxFixtures.Claim(Row("transport", "tenant-a")));

        await rig.Processor.ProcessBatchAsync(Options.BatchSize);

        rig.Recorder.Of(Delay).ShouldHaveSingleItem().Tags.ContainsKey("store.key").ShouldBeFalse();
        rig.Recorder.Of(Settled).ShouldHaveSingleItem().Tags.ContainsKey("store.key").ShouldBeFalse();
    }

    // ---------------------------------------------------------------------------
    // V4 — microkit.outbox.endpoints.backoff, microkit.outbox.tenants.backoff
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ARowWithNoTenantDown_CountsItsEndpointThroughItsHalfOpenState_UntilASuccess()
    {
        using var rig = new Rig();
        var down = true;
        rig.Script = _ => down ? new OutboxTransportUnavailableException("broker unreachable") : null;
        rig.Claims.Enqueue(OutboxFixtures.Claim(Row("transport", tenant: null)));

        await rig.Processor.ProcessBatchAsync(Options.BatchSize);

        rig.Observe(Endpoints).ShouldBe(["local=0", "transport=1"]);
        rig.Observe(Tenants).ShouldBe(["local=0", "transport=0"]);

        // The back-off is over: half-open, not recovered.
        rig.Clock.Advance(Options.PollingInterval);
        rig.Backoff.IsBlocked(OutboxDispatchers.Transport, rig.Clock.GetUtcNow(), out _)
            .ShouldBeFalse("control: the back-off has ended");
        rig.Observe(Endpoints).ShouldBe(["local=0", "transport=1"]);

        down = false;
        rig.Claims.Enqueue(OutboxFixtures.Claim(Row("transport", tenant: null)));
        await rig.Processor.ProcessBatchAsync(Options.BatchSize);

        rig.Settled[^1].Kind.ShouldBe(OutboxOutcomeKind.Published, "control: the endpoint dispatched a row");
        rig.Observe(Endpoints).ShouldBe(["local=0", "transport=0"], "a success clears it at once");
    }

    [Fact]
    public async Task ADownEndpointWithNoNewAttempt_DropsOut_ACeilingPlusMaxPollingIntervalAfterItsBackOffEnded()
    {
        using var rig = new Rig();
        rig.Script = _ => new OutboxTransportUnavailableException("broker unreachable");
        rig.Claims.Enqueue(OutboxFixtures.Claim(Row("transport", tenant: null)));
        await rig.Processor.ProcessBatchAsync(Options.BatchSize);
        rig.Backoff.IsBlocked(OutboxDispatchers.Transport, rig.Clock.GetUtcNow(), out var until).ShouldBeTrue();

        rig.Clock.SetUtcNow(until + HalfOpen - TimeSpan.FromTicks(1));
        rig.Observe(Endpoints).ShouldBe(["local=0", "transport=1"], "still within the bound");

        rig.Clock.SetUtcNow(until + HalfOpen);
        rig.Observe(Endpoints).ShouldBe(["local=0", "transport=0"], "nothing left to send: it drops out");
    }

    [Fact]
    public async Task OneTenantDown_CountsTheTenant_TheProbeFailing_CountsTheEndpoint_AndEachClearsOnItsOwnSuccessOrBound()
    {
        using var rig = new Rig();
        var down = true;
        rig.Script = _ => down ? new OutboxTransportUnavailableException("refused") : null;
        rig.Claims.Enqueue(OutboxFixtures.Claim(Row("transport", "tenant-a")));

        await rig.Processor.ProcessBatchAsync(Options.BatchSize);

        rig.Observe(Tenants).ShouldBe(["local=0", "transport=1"]);
        rig.Observe(Endpoints).ShouldBe(["local=0", "transport=0"], "one tenant down leaves the endpoint open");

        // The probe: a second tenant down before any success — the cause is shared.
        rig.Claims.Enqueue(OutboxFixtures.Claim(Row("transport", "tenant-b")));
        await rig.Processor.ProcessBatchAsync(Options.BatchSize);

        rig.Observe(Tenants).ShouldBe(["local=0", "transport=2"]);
        rig.Observe(Endpoints).ShouldBe(["local=0", "transport=1"]);

        // Every back-off over, no attempt since: half-open, still counted.
        rig.Clock.Advance(TimeSpan.FromMinutes(1));
        rig.Backoff.TenantsOf(OutboxDispatchers.Transport).BlockedAt(rig.Clock.GetUtcNow())
            .ShouldBeEmpty("control: no back-off is in force any more");
        rig.Observe(Tenants).ShouldBe(["local=0", "transport=2"]);
        rig.Observe(Endpoints).ShouldBe(["local=0", "transport=1"]);

        // Tenant A recovers: its entry and the endpoint clear at once; tenant B's stays.
        down = false;
        rig.Claims.Enqueue(OutboxFixtures.Claim(Row("transport", "tenant-a")));
        await rig.Processor.ProcessBatchAsync(Options.BatchSize);

        rig.Observe(Tenants).ShouldBe(["local=0", "transport=1"]);
        rig.Observe(Endpoints).ShouldBe(["local=0", "transport=0"]);

        // Tenant B, with nothing left to send, drops out once the bound has passed.
        rig.Backoff.TenantsOf(OutboxDispatchers.Transport).IsBlocked("tenant-b", rig.Clock.GetUtcNow(), out var untilB);
        rig.Clock.SetUtcNow(untilB + HalfOpen - TimeSpan.FromTicks(1));
        rig.Observe(Tenants).ShouldBe(["local=0", "transport=1"], "still within the bound");
        rig.Clock.SetUtcNow(untilB + HalfOpen);
        rig.Observe(Tenants).ShouldBe(["local=0", "transport=0"]);
    }

    [Fact]
    public async Task ATenantWithNothingLeftToSend_DropsOutWhenABatchPrunesIt_ACeilingAfterItsBackOffEnded()
    {
        // The documented earlier exit: another tenant's batch prunes the entry before the counter's bound.
        using var rig = new Rig();
        rig.Script = m => m.TenantId == "tenant-a" ? new OutboxTransportUnavailableException("refused") : null;
        rig.Claims.Enqueue(OutboxFixtures.Claim(Row("transport", "tenant-a")));
        await rig.Processor.ProcessBatchAsync(Options.BatchSize);
        rig.Backoff.TenantsOf(OutboxDispatchers.Transport).IsBlocked("tenant-a", rig.Clock.GetUtcNow(), out var until);

        rig.Clock.SetUtcNow(until + Options.TransportUnavailableBackoff - TimeSpan.FromTicks(1));
        rig.Claims.Enqueue(OutboxFixtures.Claim(Row("transport", "tenant-c")));
        await rig.Processor.ProcessBatchAsync(Options.BatchSize);
        rig.Observe(Tenants).ShouldBe(["local=0", "transport=1"], "not pruned yet: another tenant's success leaves it");

        rig.Clock.SetUtcNow(until + Options.TransportUnavailableBackoff);
        rig.Claims.Enqueue(OutboxFixtures.Claim(Row("transport", "tenant-c")));
        await rig.Processor.ProcessBatchAsync(Options.BatchSize);
        rig.Backoff.TenantsOf(OutboxDispatchers.Transport).IsEmpty.ShouldBeTrue("control: the batch pruned the entry");
        rig.Observe(Tenants).ShouldBe(["local=0", "transport=0"]);
    }

    // ---------------------------------------------------------------------------
    // V6 — no listener, no allocation
    // ---------------------------------------------------------------------------

    [Fact]
    public void WithNoListener_TheRecordPathsAllocateNothing()
    {
        using var metrics = new OutboxMetrics(TimeProvider.System, Options);
        metrics.DispatchDelayEnabled.ShouldBeFalse("control: nothing listens to this instance");

        static void RecordABatch(OutboxMetrics metrics)
        {
            var tally = default(OutboxMetrics.SettledTally);
            tally.Add(OutboxDispatchers.Transport, OutboxOutcomeKind.Published);
            tally.Add(OutboxDispatchers.Transport, OutboxOutcomeKind.Retry);
            tally.Add(OutboxDispatchers.Local, OutboxOutcomeKind.DeadLetter);
            tally.Add(OutboxDispatchers.Local, OutboxOutcomeKind.Deferred);
            tally.Add(null, OutboxOutcomeKind.Released);
            metrics.RecordDispatchDelay(TimeSpan.FromSeconds(1), "tests:store", OutboxDispatchers.Transport);
            metrics.RecordDispatchDelay(TimeSpan.FromSeconds(-1), null, OutboxDispatchers.Local);
            metrics.RecordSettled("tests:store", in tally);
            metrics.RecordSettled(null, in tally);
        }

        RecordABatch(metrics); // JIT and first-call work out of the measure.
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000; i++)
        {
            RecordABatch(metrics);
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).ShouldBe(0L);
    }

    /// <summary>"endpoint/outcome=value", sorted, for one exact comparison.</summary>
    private static List<string> Cells(List<MeterRecorder.Recorded> recorded)
        => [.. recorded.Select(r => $"{r.Tag("endpoint")}/{r.Tag("outcome")}={r.Value}").Order(StringComparer.Ordinal)];

    /// <summary>A processor over a recording store, both dispatchers scripted, its own meter listened to.</summary>
    private sealed class Rig : IDisposable
    {
        public Rig(bool withStore = true)
        {
            var storeKey = withStore ? Store : null;
            Backoff = new OutboxEndpointBackoff(Options.PollingInterval, Options.TransportUnavailableBackoff);
            Metrics = new OutboxMetrics(Clock, Options);
            if (storeKey is not null)
            {
                Metrics.Track(storeKey, Backoff);
            }

            Recorder = new MeterRecorder(Metrics.Meter);

            var store = Substitute.For<IOutboxProcessorStore>();
            store.ClaimBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<OutboxEndpoints>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
                .Returns(_ => ValueTask.FromResult(Claims.Count > 0 ? Claims.Dequeue() : OutboxFixtures.Claim()));
            store.ApplyOutcomesAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<OutboxOutcome>>(), Arg.Any<CancellationToken>())
                .Returns(ci =>
                {
                    var outcomes = ci.Arg<IReadOnlyList<OutboxOutcome>>();
                    Settled.AddRange(outcomes);
                    return ValueTask.FromResult(Settle(outcomes));
                });

            var services = new ServiceCollection();
            services.AddScoped<DeliveryStoreHolder>();
            services.AddKeyedSingleton<IOutboxDispatcher>(OutboxDispatchers.Transport, new ScriptedDispatcher(m => Script?.Invoke(m)));
            services.AddKeyedSingleton<IOutboxDispatcher>(OutboxDispatchers.Local, new ScriptedDispatcher(m => Script?.Invoke(m)));
            var provider = services.BuildServiceProvider();

            Processor = new OutboxProcessor(
                store,
                new TestExecutionScopeFactory(provider.GetRequiredService<IServiceScopeFactory>()),
                Options,
                Clock,
                FixedRandom.NoJitter,
                NullLogger<OutboxProcessor>.Instance,
                storeKey,
                Backoff,
                Metrics);
        }

        public FakeTimeProvider Clock { get; } = new(Now);

        public OutboxEndpointBackoff Backoff { get; }

        public OutboxMetrics Metrics { get; }

        public MeterRecorder Recorder { get; }

        public OutboxProcessor Processor { get; }

        public Queue<OutboxClaim> Claims { get; } = new();

        public List<OutboxOutcome> Settled { get; } = [];

        public Func<IReadOnlyList<OutboxOutcome>, int> Settle { get; init; } = outcomes => outcomes.Count;

        public Func<OutboxMessage, Exception?>? Script { get; set; }

        /// <summary>"endpoint=value" of this rig's store, sorted.</summary>
        public List<string> Observe(string instrument)
        {
            var observed = Recorder.Observe(instrument);
            observed.ShouldAllBe(r => r.Tag("store.key") == Store.Value);
            return [.. observed.Select(r => $"{r.Tag("endpoint")}={r.Value}").Order(StringComparer.Ordinal)];
        }

        public void Dispose()
        {
            Recorder.Dispose();
            Metrics.Dispose();
        }
    }
}
