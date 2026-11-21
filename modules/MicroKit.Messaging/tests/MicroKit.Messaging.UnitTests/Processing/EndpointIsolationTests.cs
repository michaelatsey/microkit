// NSubstitute ValueTask setup chains (.Returns on the method-call result) are intentional.
#pragma warning disable CA2012
namespace MicroKit.Messaging.UnitTests.Processing;

using Microsoft.Extensions.Time.Testing;
using MicroKit.Messaging.Stores;

/// <summary>
/// One endpoint down never holds back the other's rows (#188): a store's transport rows that cannot
/// be sent are deferred to the end of the endpoint's back-off, no attempt counted, while its local rows
/// are delivered; a local consumer's dependency outage never holds the transport rows back; and a down
/// endpoint is tried again once per back-off period, not once per cycle. The claim-level proof, over
/// the real store, is <c>EndpointOutageClaimTests</c> in the integration suite.
/// </summary>
public sealed class EndpointIsolationTests
{
    private static readonly MessageStoreKey Store = new("tests:isolation");

    private static OutboxMessage Local()
    {
        var row = OutboxFixtures.Message();
        row.Endpoint = OutboxMessage.LocalEndpoint;
        return row;
    }

    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    private sealed class Rig
    {
        public List<OutboxOutcome> Settled { get; } = [];
        public List<OutboxEndpoints> Excluded { get; } = [];
        public IOutboxProcessorStore Store { get; set; } = null!;
        public int TransportActivations;
    }

    private static (OutboxProcessor Processor, List<OutboxOutcome> Settled, ScriptedDispatcher LocalDispatcher, ScriptedDispatcher Transport, CapturingLogger<OutboxProcessor> Logs)
        Build(OutboxClaim claim, bool transportActivates, Func<OutboxMessage, Exception?>? localScript = null, Func<OutboxMessage, Exception?>? transportScript = null)
    {
        var (processor, rig, local, transport, logs) = Build(
            claim, transportActivates, new FakeTimeProvider(Now), BackoffOf(new OutboxProcessorOptions()), localScript, transportScript);
        return (processor, rig.Settled, local, transport, logs);
    }

    private static OutboxEndpointBackoff BackoffOf(OutboxProcessorOptions options)
        => new(options.PollingInterval, options.TransportUnavailableBackoff);

    private static (OutboxProcessor Processor, Rig Rig, ScriptedDispatcher LocalDispatcher, ScriptedDispatcher Transport, CapturingLogger<OutboxProcessor> Logs)
        Build(OutboxClaim claim, bool transportActivates, FakeTimeProvider clock, OutboxEndpointBackoff backoff, Func<OutboxMessage, Exception?>? localScript = null, Func<OutboxMessage, Exception?>? transportScript = null)
    {
        var rig = new Rig();
        var settled = rig.Settled;
        var store = Substitute.For<IOutboxProcessorStore>();
        rig.Store = store;
        // A store that does not honour the filter (against the contract): the processor still defers
        // the excluded endpoint's rows.
        store.ClaimBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<OutboxEndpoints>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                rig.Excluded.Add(ci.Arg<OutboxEndpoints>());
                return ValueTask.FromResult(claim);
            });
        store.ApplyOutcomesAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<OutboxOutcome>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                settled.AddRange(ci.Arg<IReadOnlyList<OutboxOutcome>>());
                return ValueTask.FromResult(settled.Count);
            });

        var local = new ScriptedDispatcher(localScript);
        var transport = new ScriptedDispatcher(transportScript);
        var services = new ServiceCollection();
        services.AddScoped<DeliveryStoreHolder>();
        services.AddKeyedSingleton<IOutboxDispatcher>(OutboxDispatchers.Local, local);
        // A missing IMessageTransport behind the store's transport dispatcher: the container's
        // InvalidOperationException at activation.
        services.AddKeyedScoped<IOutboxDispatcher>(OutboxDispatchers.Transport, (_, _) =>
        {
            Interlocked.Increment(ref rig.TransportActivations);
            return transportActivates
                ? transport
                : throw new InvalidOperationException("Unable to resolve service for type 'IMessageTransport'.");
        });

        var provider = services.BuildServiceProvider();
        var logs = new CapturingLogger<OutboxProcessor>();
        var processor = new OutboxProcessor(
            store,
            new TestExecutionScopeFactory(provider.GetRequiredService<IServiceScopeFactory>()),
            new OutboxProcessorOptions(),
            clock,
            FixedRandom.NoJitter,
            logs,
            Store,
            backoff);
        return (processor, rig, local, transport, logs);
    }

    /// <summary>A deferred row: back to pending at the end of the back-off, no attempt counted.</summary>
    private static void ShouldBeDeferred(OutboxOutcome outcome, DateTimeOffset until)
    {
        outcome.Kind.ShouldBe(OutboxOutcomeKind.Deferred, "the store keeps its retry count and error text");
        outcome.NextRetryAtUtc.ShouldBe(until, "the claim skips it until then");
    }

    [Fact]
    public async Task AMixedBatchWithNoTransport_DeliversItsLocalRows_AndDefersOnlyItsTransportRows()
    {
        var transportFirst = OutboxFixtures.ContractMessage();
        var localA = Local();
        var transportSecond = OutboxFixtures.ContractMessage();
        var localB = Local();
        var (processor, settled, local, _, logs) = Build(
            OutboxFixtures.Claim(transportFirst, localA, transportSecond, localB), transportActivates: false);

        var result = await processor.ProcessBatchAsync(10);

        local.Dispatched.ShouldBe([localA, localB], "the local rows are delivered, in claim order");
        settled.Single(o => o.MessageId == localA.Id).Kind.ShouldBe(OutboxOutcomeKind.Published);
        settled.Single(o => o.MessageId == localB.Id).Kind.ShouldBe(OutboxOutcomeKind.Published);
        var until = Now + new OutboxProcessorOptions().PollingInterval;
        ShouldBeDeferred(settled.Single(o => o.MessageId == transportFirst.Id), until);
        ShouldBeDeferred(settled.Single(o => o.MessageId == transportSecond.Id), until);
        settled.Count.ShouldBe(4, "every claimed row carries exactly one outcome");
        result.Published.ShouldBe(2);
        result.Released.ShouldBe(2, "a deferred row is reported released: no attempt is charged to it");
        result.Retried.ShouldBe(0, "no retry budget moves");
        result.AbortReason.ShouldBe(OutboxBatchAbortReason.DispatcherActivationFailed);
        result.HandedOverInBackOff.ShouldBeFalse("a back-off that began in this batch: its rows wait out their next attempt");
        logs.Records.Count(r => r.EventId == 1009).ShouldBe(1, "one activation failure for the blocked endpoint");
        logs.Records.Single(r => r.EventId == 1010).Message
            .ShouldContain("2 row(s) of the batch were settled to wait", Case.Sensitive, "deferral has its own event, counting what it deferred");
    }

    [Fact]
    public async Task ALocalConsumersDependencyOutage_NeverHoldsBackTheTransportRows()
    {
        var localA = Local();
        var transport = OutboxFixtures.ContractMessage();
        var localB = Local();
        var (processor, settled, local, transportDispatcher, _) = Build(
            OutboxFixtures.Claim(localA, transport, localB),
            transportActivates: true,
            localScript: _ => new OutboxTransportUnavailableException("a consumer's dependency is down"));

        await processor.ProcessBatchAsync(10);

        transportDispatcher.Dispatched.ShouldBe([transport]);
        local.Dispatched.ShouldBe([localA], "the local endpoint is down for the rest of the batch");
        settled.Single(o => o.MessageId == transport.Id).Kind.ShouldBe(OutboxOutcomeKind.Published);
        settled.Where(o => o.MessageId != transport.Id).ShouldAllBe(o => o.Kind == OutboxOutcomeKind.Deferred);
    }

    [Fact]
    public async Task ADownEndpoint_IsTriedOncePerBackOffPeriod_NotOncePerCycle_AndItsBackOffGrows()
    {
        var options = new OutboxProcessorOptions();
        var clock = new FakeTimeProvider(Now);
        var backoff = BackoffOf(options);
        var transportRows = Enumerable.Range(0, 3).Select(_ => OutboxFixtures.ContractMessage()).ToArray();

        async Task<Rig> CycleAsync()
        {
            var (processor, rig, _, _, _) = Build(OutboxFixtures.Claim(transportRows), transportActivates: false, clock, backoff);
            await processor.ProcessBatchAsync(10);
            return rig;
        }

        (await CycleAsync()).TransportActivations.ShouldBe(1, "tried once, then its other rows are deferred");

        clock.Advance(options.PollingInterval / 2);
        var inBackOff = await CycleAsync();
        inBackOff.Excluded.ShouldBe([OutboxEndpoints.Transport], "the claim leaves the endpoint in back-off out");
        inBackOff.TransportActivations.ShouldBe(0, "inside its back-off the endpoint is not tried");
        inBackOff.Settled.ShouldAllBe(o => o.Kind == OutboxOutcomeKind.Deferred && o.NextRetryAtUtc == Now + options.PollingInterval);

        clock.Advance(options.PollingInterval);
        var retried = await CycleAsync();
        retried.Excluded.ShouldBe([OutboxEndpoints.None], "its back-off over, the claim includes it again");
        retried.TransportActivations.ShouldBe(1, "its back-off over, it is tried again");
        var doubled = clock.GetUtcNow() + (options.PollingInterval * 2);
        retried.Settled.ShouldAllBe(o => o.NextRetryAtUtc == doubled, "the back-off doubled");
    }

    [Fact]
    public async Task RowsOfAnEndpointInBackOff_HandedOverAnyway_AreDeferredAndReported()
    {
        // A store that does not honour the claim's endpoint filter — against its contract — hands
        // them over.
        var options = new OutboxProcessorOptions();
        var clock = new FakeTimeProvider(Now);
        var backoff = BackoffOf(options);
        var until = backoff.Block(OutboxDispatchers.Transport, Now);
        var rows = new[] { OutboxFixtures.ContractMessage(), OutboxFixtures.ContractMessage() };
        var (processor, rig, _, _, logs) = Build(OutboxFixtures.Claim(rows), transportActivates: true, clock, backoff);

        var result = await processor.ProcessBatchAsync(10);

        rig.Settled.ShouldAllBe(o => o.Kind == OutboxOutcomeKind.Deferred && o.NextRetryAtUtc == until);
        result.AbortReason.ShouldBe(OutboxBatchAbortReason.EndpointInBackOff, "never silent in the result");
        result.HandedOverInBackOff.ShouldBeTrue("the loop waits rather than polling again at once");
        logs.Records.Single(r => r.EventId == 1010).Message
            .ShouldContain("2 row(s) of the batch were settled to wait", Case.Sensitive, "never silent in the logs");
    }

    [Fact]
    public async Task ADeferralThatWasNotWritten_IsNotReportedAsDeferred()
    {
        // A lost lease: the settlement writes none of the batch's rows.
        var rows = new[] { OutboxFixtures.ContractMessage(), OutboxFixtures.ContractMessage() };
        var (processor, rig, _, _, logs) = Build(
            OutboxFixtures.Claim(rows), transportActivates: false, new FakeTimeProvider(Now), BackoffOf(new OutboxProcessorOptions()));
        rig.Store.ApplyOutcomesAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<OutboxOutcome>>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(0));

        await processor.ProcessBatchAsync(10);

        logs.Records.ShouldNotContain(r => r.EventId == 1010, "1010 reports only a deferral that was written");
        logs.Records.ShouldContain(r => r.EventId == 1009, "the outage itself is still reported");
    }

    [Fact]
    public async Task A1010_ReportsTheEarliestAndTheLatestNextAttempt_WhenABackOffEndsMidBatch()
    {
        var options = new OutboxProcessorOptions();
        var clock = new FakeTimeProvider(Now);
        var first = OutboxFixtures.ContractMessage();
        var slow = Local();
        var second = OutboxFixtures.ContractMessage();
        var (processor, _, _, _, logs) = Build(
            OutboxFixtures.Claim(first, slow, second), transportActivates: false, clock, BackoffOf(options),
            localScript: _ =>
            {
                clock.Advance(options.PollingInterval + TimeSpan.FromSeconds(1));   // the first back-off ends
                return null;
            });

        await processor.ProcessBatchAsync(10);

        var earliest = Now + options.PollingInterval;
        var latest = clock.GetUtcNow() + (options.PollingInterval * 2);
        var deferral = logs.Records.Single(r => r.EventId == 1010).Message;
        deferral.ShouldContain(earliest.ToString("O", System.Globalization.CultureInfo.InvariantCulture), Case.Sensitive);
        deferral.ShouldContain(latest.ToString("O", System.Globalization.CultureInfo.InvariantCulture), Case.Sensitive);
        deferral.ShouldContain("2 row(s) of the batch", Case.Sensitive);
        logs.Records.Count(r => r.EventId == 1009).ShouldBe(2, "its back-off over, the second row was attempted, not deferred");
    }

    [Fact]
    public async Task RowsDeferredForAnEndpointNotLeftOutOfTheClaim_DoNotFlagTheBatch()
    {
        // The transport is in back-off, so the claim leaves it out; the store hands over local rows
        // only. A local outage met within the batch defers the later local rows: the endpoint the
        // claim excluded is not theirs, so nothing tells the loop to wait.
        var options = new OutboxProcessorOptions();
        var clock = new FakeTimeProvider(Now);
        var backoff = BackoffOf(options);
        backoff.Block(OutboxDispatchers.Transport, Now);
        var (processor, rig, local, _, _) = Build(
            OutboxFixtures.Claim(Local(), Local(), Local()), transportActivates: true, clock, backoff,
            localScript: _ => new OutboxTransportUnavailableException("a consumer's dependency is down"));

        var result = await processor.ProcessBatchAsync(10);

        rig.Excluded.ShouldBe([OutboxEndpoints.Transport]);
        local.Dispatched.Count.ShouldBe(1, "the later local rows went through the in-back-off path, unattempted");
        result.AbortReason.ShouldBe(OutboxBatchAbortReason.TransportUnavailable);
        rig.Settled.ShouldAllBe(o => o.Kind == OutboxOutcomeKind.Deferred);
        result.HandedOverInBackOff.ShouldBeFalse("only a row of an excluded endpoint sets it");
    }

    [Fact]
    public async Task RowsHandedOverInBackOff_AfterTheOtherEndpointFailedFirst_StillFlagTheBatch()
    {
        // The first verdict is the local endpoint's; the transport rows behind it were handed over in
        // back-off by a store that does not honour the filter. The flag, not the reason, tells the loop.
        var options = new OutboxProcessorOptions();
        var clock = new FakeTimeProvider(Now);
        var backoff = BackoffOf(options);
        backoff.Block(OutboxDispatchers.Transport, Now);
        var (processor, _, _, _, _) = Build(
            OutboxFixtures.Claim(Local(), OutboxFixtures.ContractMessage(), OutboxFixtures.ContractMessage()),
            transportActivates: true, clock, backoff,
            localScript: _ => new OutboxTransportUnavailableException("a consumer's dependency is down"));

        var result = await processor.ProcessBatchAsync(10);

        result.AbortReason.ShouldBe(OutboxBatchAbortReason.TransportUnavailable);
        result.HandedOverInBackOff.ShouldBeTrue();
    }

    [Fact]
    public async Task AnOutageMetLateInALongBatch_IsBackedOffFromTheTimeOfTheFailure()
    {
        // A batch that runs past the polling interval before its transport row fails must not defer
        // that row to a time already passed: the down broker would be retried at once.
        var options = new OutboxProcessorOptions();
        var clock = new FakeTimeProvider(Now);
        var slow = Local();
        var transport = OutboxFixtures.ContractMessage();
        var (processor, rig, _, _, _) = Build(
            OutboxFixtures.Claim(slow, transport), transportActivates: false, clock, BackoffOf(options),
            localScript: _ =>
            {
                clock.Advance(options.PollingInterval * 3);
                return null;
            });

        await processor.ProcessBatchAsync(10);

        rig.Settled.Single(o => o.MessageId == transport.Id).NextRetryAtUtc
            .ShouldBe(clock.GetUtcNow() + options.PollingInterval);
    }

    [Fact]
    public void AnOutageLongAfterTheLastOne_StartsFromTheBaseDelay()
    {
        var options = new OutboxProcessorOptions();
        var backoff = BackoffOf(options);
        var first = backoff.Block(OutboxDispatchers.Transport, Now);
        var second = backoff.Block(OutboxDispatchers.Transport, first);
        second.ShouldBe(first + (options.PollingInterval * 2), "retried as its back-off ended: it doubles");

        // Retried late — the loop's idle polling stretches the gap — it still doubles.
        var late = second + options.MaxPollingInterval;
        var third = backoff.Block(OutboxDispatchers.Transport, late);
        third.ShouldBe(late + (options.PollingInterval * 4), "a late retry is the same outage");

        // A whole ceiling past the end of its back-off without failing again: a new outage.
        var later = third + options.TransportUnavailableBackoff;
        backoff.Block(OutboxDispatchers.Transport, later).ShouldBe(later + options.PollingInterval);
    }

    [Fact]
    public async Task AnEndpointThatDeliversAgain_ClearsItsBackOff()
    {
        var options = new OutboxProcessorOptions();
        var clock = new FakeTimeProvider(Now);
        var backoff = BackoffOf(options);
        var row = OutboxFixtures.ContractMessage();

        var (down, _, _, _, _) = Build(OutboxFixtures.Claim(row), transportActivates: false, clock, backoff);
        await down.ProcessBatchAsync(10);
        clock.Advance(options.PollingInterval);
        var (up, _, _, _, _) = Build(OutboxFixtures.Claim(row), transportActivates: true, clock, backoff);
        await up.ProcessBatchAsync(10);

        backoff.IsBlocked(OutboxDispatchers.Transport, clock.GetUtcNow(), out _).ShouldBeFalse();
        backoff.Block(OutboxDispatchers.Transport, clock.GetUtcNow())
            .ShouldBe(clock.GetUtcNow() + options.PollingInterval, "the next outage starts from the base delay");
    }
}
