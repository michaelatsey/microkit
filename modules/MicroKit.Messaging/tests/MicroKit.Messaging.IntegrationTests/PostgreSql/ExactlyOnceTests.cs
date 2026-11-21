using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroKit.Messaging.IntegrationTests.PostgreSql;

/// <summary>
/// V1 (#188 D6): each handler's database effects apply exactly once — across a host lost between
/// the business commit and the dispatch, a redelivery, concurrent deliveries, a retry abandoned
/// with its claim, and a commit fault on either side of the database under a retrying strategy.
/// </summary>
[Collection(PostgreSqlSuite.Name)]
public sealed class ExactlyOnceTests(PostgreSqlFixture fixture)
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Host A commits the business data and the publication, and is gone before it drains. Host B on
    /// the same database drains the outbox, and each handler's effect applies once. A redelivery of
    /// the processed message is then skipped.
    /// </summary>
    [DockerRequiredFact]
    public async Task AHostLostBetweenTheCommitAndTheDispatch_IsDrainedByAnother_AndEachEffectAppliesOnce()
    {
        await using var db = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
        var broker = new InMemoryBroker();
        var message = new GuardEvent(Guid.NewGuid());

        // Host A: the business commit and the enlisted publication, one SaveChanges, then nothing.
        await using (var hostA = GuardHost.Create(db, handlers: _ => { }, broker: broker))
        {
            await using var scope = hostA.Provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TestMessagingDbContext>();
            await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(message);
            context.BusinessRecords.Add(new BusinessRecord { Id = Guid.NewGuid(), Value = "order" });
            await context.SaveChangesAsync();
        }

        (await OutboxOf(db)).ShouldHaveSingleItem().Status.ShouldBe(OutboxMessageStatus.Pending, "host A never drained");

        // Host B: the same database drains.
        await using var hostB = GuardHost.Create(db, broker: broker);
        await hostB.StartAsync();
        await Eventually(async () => (await hostB.InboxAsync()).Count(r => r.Status == InboxMessageStatus.Processed) == 2);

        var effects = await hostB.EffectsAsync();
        effects.Count(e => e == $"first:{message.Id}").ShouldBe(1);
        effects.Count(e => e == $"second:{message.Id}").ShouldBe(1);
        effects.Count(e => e == "order").ShouldBe(1);

        // The processed message, delivered again: skipped, every effect still once.
        var row = (await hostB.OutboxAsync()).ShouldHaveSingleItem();
        var redelivery = await hostB.ReceiveAsync(GuardHost.Envelope(row.Id.Value, message));

        redelivery.Disposition.ShouldBe(ReceiveDisposition.Acknowledge);
        hostB.Probe.Calls(FirstGuardHandler.HandlerName).ShouldBe(1);
        hostB.Probe.Calls(SecondGuardHandler.HandlerName).ShouldBe(1);
        (await hostB.EffectsAsync()).Count.ShouldBe(3);
    }

    /// <summary>
    /// C4: two deliveries of one message, both past the D6a/b read — the barrier is inside the
    /// handler, which runs only after the read — before either saves. One commits; the other's save
    /// hits the unique index, is classified a duplicate (2201), and rolls back its effect.
    /// </summary>
    [DockerRequiredFact]
    public async Task TwoConcurrentDeliveries_BothPastTheRead_GiveOneEffect()
    {
        await using var db = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
        await using var host = GuardHost.Create(db, handlers: bus => bus.AddHandler<FirstGuardHandler>());
        var message = new GuardEvent(Guid.NewGuid());
        var envelope = GuardHost.Envelope(Guid.NewGuid(), message);

        var bothPastTheRead = new Barrier(2);
        host.Probe.Scripts[FirstGuardHandler.HandlerName] = _ =>
        {
            bothPastTheRead.SignalAndWait(Wait).ShouldBeTrue("both deliveries must reach the handler");
            return Task.CompletedTask;
        };

        var results = await Task.WhenAll(
            Task.Run(async () => await host.ReceiveAsync(envelope)),
            Task.Run(async () => await host.ReceiveAsync(envelope)));

        results.ShouldAllBe(r => r.Disposition == ReceiveDisposition.Acknowledge);
        host.Probe.Calls(FirstGuardHandler.HandlerName).ShouldBe(2, "both ran: neither saw a row at the read");
        (await host.EffectsAsync()).ShouldHaveSingleItem().ShouldBe($"first:{message.Id}");
        (await host.InboxAsync()).ShouldHaveSingleItem().Status.ShouldBe(InboxMessageStatus.Processed);

        var duplicate = host.Logs.Entries.Where(e => e.EventId == 2201).ShouldHaveSingleItem();
        duplicate.Level.ShouldBe(LogLevel.Debug);
        host.Logs.Entries.ShouldNotContain(e => e.EventId == 2208 || e.EventId == 2202,
            "the loser is a duplicate (D6d), never a failure");
    }

    /// <summary>
    /// C4: a retry claimed with a short lease is abandoned mid-handler — the host that claimed it is
    /// gone. After the lease expires another host claims and handles it. When the abandoned attempt
    /// finally commits, the claim token rejects it: one effect, and no attempt counted.
    /// </summary>
    [DockerRequiredFact]
    public Task ARetryAbandonedWithItsClaim_IsRecoveredAfterTheLease_AndTheEffectAppliesOnce()
        => RecoverAbandonedClaimAsync(offsetOfB: TimeSpan.Zero, stepBackBeforeTheClaim: false);

    /// <summary>
    /// The same recovery with B's clock 5 s behind A's — what a backward step of the wall clock between
    /// A's claim and B's does. WSL2 stepped this machine's back 402–619 ms every ~32 s; the old fixed
    /// wait (lease × 2 after A entered its handler) tolerated a step of the lease plus A's
    /// claim-to-handler time, tens of milliseconds once the suite is warm, so such a step left B's claim
    /// before A's lease and B claimed nothing. 5 s exceeds that margin however cold the run, and leaves
    /// the probe below seconds of slack before B's clock reaches A's lease. A first
    /// claim, made once A's lease is past by the system clock but not by B's, must take nothing: B's
    /// claim reads B's clock. The recovery is only later, never lost, and the effect applies once.
    /// </summary>
    [DockerRequiredFact]
    public Task ARetryAbandonedWithItsClaim_IsRecoveredAfterTheLease_WhenTheWallClockStepsBack()
        => RecoverAbandonedClaimAsync(offsetOfB: TimeSpan.FromSeconds(-5), stepBackBeforeTheClaim: false);

    /// <summary>
    /// B's clock steps back, between the test's check and B's claim, to 5 s before A's lease, so that
    /// claim finds A's lease live and takes nothing; a later one recovers the row, and the effect still
    /// applies once.
    /// </summary>
    [DockerRequiredFact]
    public Task ARetryAbandonedWithItsClaim_IsRecoveredAfterTheLease_WhenTheClockStepsBackBeforeTheClaim()
        => RecoverAbandonedClaimAsync(offsetOfB: TimeSpan.Zero, stepBackBeforeTheClaim: true);

    private async Task RecoverAbandonedClaimAsync(TimeSpan offsetOfB, bool stepBackBeforeTheClaim)
    {
        await using var db = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));

        // A's lease only has to expire: nothing in A's attempt checks its expiry, only its token, so
        // the test waits for the expiry itself (below), never for a fixed multiple of it. Short, so
        // the wait is short.
        //
        // The lease is wall-clock time — stamped from the system clock, compared with it — and the
        // wall clock can step backwards: WSL2 pulled this machine's back 402–619 ms every ~32 s
        // (journald "Time jumped backwards"), which once left B's claim before A's lease with the
        // old fixed wait (lease × 2). So B runs on a recording clock: the test waits on it, and an
        // empty claim is retried only when the `now` that claim read was not past A's lease — a clock
        // that went back, never the defect this test exists to catch. Deadlines are monotonic.
        var lease = TimeSpan.FromMilliseconds(300);
        var margin = TimeSpan.FromMilliseconds(100);
        var clockOfB = new RecordingClock(offsetOfB);
        await using var hostA = GuardHost.Create(db, handlers: bus => bus.AddHandler<FirstGuardHandler>(), lease: lease);
        await using var hostB = GuardHost.Create(db, handlers: bus => bus.AddHandler<FirstGuardHandler>(), clock: clockOfB);
        var message = new GuardEvent(Guid.NewGuid());
        var timeline = new ClaimTimeline(lease, Wait);
        timeline.Mark($"B's clock: {offsetOfB.TotalMilliseconds} ms off the system clock; a step before the claim: {stepBackBeforeTheClaim}");
        await hostA.SeedFailedAsync<FirstGuardHandler>(Guid.NewGuid(), message, retryCount: 1);
        await timeline.RowAsync("seeded", hostB);

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hostA.Probe.Scripts[FirstGuardHandler.HandlerName] = async _ =>
        {
            entered.TrySetResult();
            await release.Task;
        };

        var abandoned = hostA.RetryOnceAsync();
        try
        {
            // A's batch is watched too: one that ends without entering the handler fails at once,
            // with its result, rather than after the whole wait.
            if (await Task.WhenAny(entered.Task, abandoned, Task.Delay(Wait)) != entered.Task)
            {
                var ended = abandoned.IsCompletedSuccessfully ? abandoned.Result.ToString()
                    : abandoned.IsFaulted ? abandoned.Exception!.GetBaseException().ToString()
                    : abandoned.Status.ToString();
                throw new ShouldAssertException($"A never entered its handler; A's batch: {ended}" + timeline);
            }

            var claimedByA = await timeline.RowAsync("A claimed it and entered its handler", hostB);
            claimedByA.Status.ShouldBe(InboxMessageStatus.Processing, timeline.ToString());
            claimedByA.LockedUntilUtc.ShouldNotBeNull(timeline.ToString());
            var leaseOfA = claimedByA.LockedUntilUtc.Value;

            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            var logMark = hostB.Logs.Mark();
            ShouldAssertException Fail(string why)
            {
                timeline.Logs("B", hostB.Logs.Since(logMark, LogLevel.Information));
                return new ShouldAssertException(why + timeline);
            }

            async Task UntilPastLeaseAsync(TimeProvider clock, string whose)
            {
                while (clock.GetUtcNow() <= leaseOfA + margin)
                {
                    if (elapsed.Elapsed >= Wait)
                    {
                        throw Fail($"A's lease never passed by {whose} clock");
                    }

                    await Task.Delay(10);
                }
            }

            async Task<(InboxBatchResult Result, DateTimeOffset ReadByTheClaim)> ClaimAsync()
            {
                clockOfB.Arm();
                InboxBatchResult result;
                try
                {
                    var budget = Wait - elapsed.Elapsed;
                    result = await hostB.RetryOnceAsync().WaitAsync(budget > TimeSpan.Zero ? budget : TimeSpan.FromSeconds(1));
                }
                catch (TimeoutException)
                {
                    throw Fail("B's claim did not return");
                }

                var read = clockOfB.FirstReadSinceArmed ?? throw Fail("B's claim never read B's clock");
                timeline.Mark($"B's batch: {result}; its claim read B's clock at {read:HH:mm:ss.fff}");
                return (result, read);
            }

            if (offsetOfB < TimeSpan.Zero)
            {
                // A's lease past by the system clock, not by B's: B's claim must see it live.
                await UntilPastLeaseAsync(TimeProvider.System, "the system");
                var (probe, read) = await ClaimAsync();
                probe.Claimed.ShouldBe(0, "B's claim reads B's clock, still before A's lease" + timeline);
                read.ShouldBeLessThan(leaseOfA, timeline.ToString());
            }

            var stepToInject = stepBackBeforeTheClaim;
            InboxBatchResult recovered;
            while (true)
            {
                await UntilPastLeaseAsync(clockOfB, "B's");
                var injected = stepToInject;
                if (injected)
                {
                    // Relative to the lease, not to wherever the check ended: seconds of slack.
                    clockOfB.Offset += leaseOfA - TimeSpan.FromSeconds(5) - clockOfB.GetUtcNow();
                    stepToInject = false;
                    timeline.Mark("B's clock steps back to 5 s before A's lease, before its claim");
                }

                var (result, read) = await ClaimAsync();
                recovered = result;
                if (injected)
                {
                    recovered.Claimed.ShouldBe(0, "the claim right after the step finds A's lease live" + timeline);
                }

                if (recovered.Claimed > 0)
                {
                    break;
                }

                await timeline.RowAsync("B claimed nothing", hostB);
                // The store claims at LockedUntilUtc <= now: a read at or past the lease explains nothing.
                if (read >= leaseOfA)
                {
                    throw Fail($"B claimed nothing although its claim read {read:HH:mm:ss.fff}, past A's lease");
                }

                if (elapsed.Elapsed >= Wait)
                {
                    throw Fail("B claimed nothing before the deadline, each claim reading a clock before A's lease");
                }
            }

            await timeline.RowAsync("after B's batch", hostB);
            timeline.Logs("B", hostB.Logs.Since(logMark, LogLevel.Information));
            recovered.Processed.ShouldBe(1, "the expired claim is recovered" + timeline);
        }
        finally
        {
            // A's handler waits on this: released whatever happened above, and A's batch given the
            // time to end before the hosts and the database are disposed. Its outcome is asserted
            // below; on a failure above, that failure is the one reported.
            release.TrySetResult();
            await Task.WhenAny(abandoned, Task.Delay(Wait));
        }

        var late = await abandoned.WaitAsync(Wait);
        timeline.Mark($"A's late batch: {late}");
        await timeline.RowAsync("after A settled", hostB);

        late.LeasesLost.ShouldBe(1, "the abandoned attempt's claim token no longer matches" + timeline);
        late.Retried.ShouldBe(0, timeline.ToString());
        (await hostB.EffectsAsync()).ShouldHaveSingleItem(timeline.ToString()).ShouldBe($"first:{message.Id}");
        var row = (await hostB.InboxAsync()).ShouldHaveSingleItem(timeline.ToString());
        row.Status.ShouldBe(InboxMessageStatus.Processed, timeline.ToString());
        row.RetryCount.ShouldBe(1, "a lost claim counts no attempt" + timeline);
    }

    /// <summary>
    /// The system clock, shifted by <see cref="Offset"/> — a wall clock that has stepped — that records
    /// the first reading taken after <see cref="Arm"/>: the <c>now</c> a claim compared leases with.
    /// </summary>
    private sealed class RecordingClock(TimeSpan offset) : TimeProvider
    {
        private long _firstReadTicks;
        private int _armed;

        public TimeSpan Offset { get; set; } = offset;

        public DateTimeOffset? FirstReadSinceArmed
            => Volatile.Read(ref _armed) == 0 && Interlocked.Read(ref _firstReadTicks) != 0
                ? new DateTimeOffset(Interlocked.Read(ref _firstReadTicks), TimeSpan.Zero)
                : null;

        public void Arm()
        {
            Interlocked.Exchange(ref _firstReadTicks, 0);
            Volatile.Write(ref _armed, 1);
        }

        public override DateTimeOffset GetUtcNow()
        {
            var now = System.GetUtcNow() + Offset;
            if (Interlocked.CompareExchange(ref _armed, 0, 1) == 1)
            {
                Interlocked.Exchange(ref _firstReadTicks, now.UtcTicks);
            }

            return now;
        }
    }

    /// <summary>
    /// The row's history in one test, with timestamps, so a failure explains itself: each step's row
    /// state, both batch results, B's logs, and the lease and wait the test ran with.
    /// </summary>
    private sealed class ClaimTimeline(TimeSpan lease, TimeSpan wait)
    {
        private readonly List<string> _lines = [];
        private readonly System.Diagnostics.Stopwatch _monotonic = System.Diagnostics.Stopwatch.StartNew();

        /// <summary>Wall and monotonic time side by side: a clock step shows as the two diverging.</summary>
        public void Mark(string what)
            => _lines.Add($"wall {DateTimeOffset.UtcNow:HH:mm:ss.fff} mono +{_monotonic.ElapsedMilliseconds,5} ms  {what}");

        public async Task<InboxMessage> RowAsync(string what, GuardHost host)
        {
            var row = (await host.InboxAsync()).Single();
            Mark($"{what}: Status={row.Status} LockedUntilUtc={row.LockedUntilUtc:HH:mm:ss.fff} " +
                 $"ClaimToken={row.ClaimToken} NextRetryAtUtc={row.NextRetryAtUtc:HH:mm:ss.fff} RetryCount={row.RetryCount}");
            return row;
        }

        public void Logs(string host, IEnumerable<CapturedLogEntry> entries)
        {
            foreach (var entry in entries)
            {
                Mark($"{host} log {entry.EventId} {entry.Level}: {entry.RenderedMessage} {entry.ExceptionTypeName}");
            }
        }

        public override string ToString()
            => $"{Environment.NewLine}lease={lease.TotalMilliseconds} ms, wait={wait.TotalSeconds} s{Environment.NewLine}" +
               string.Join(Environment.NewLine, _lines);
    }

    /// <summary>
    /// A transient fault injected at the commit, before it reaches the database: the retrying
    /// strategy verifies (no Processed row), replays the attempt on a fresh scope, and the effect and
    /// the cascade apply once. The delivery is acknowledged only once the row is Processed.
    /// </summary>
    [DockerRequiredFact]
    public async Task WithARetryingStrategy_AFaultBeforeTheCommit_IsReplayed_AndEveryEffectAppliesOnce()
    {
        await using var db = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
        await using var host = GuardHost.Create(db, handlers: bus => bus.AddHandler<FirstGuardHandler>(), retrying: true, broker: new InMemoryBroker());
        host.Probe.PublishFollowUp = true;
        var message = new GuardEvent(Guid.NewGuid());

        host.Faults.Arm(CommitFaultPoint.BeforeCommit);
        var result = await host.ReceiveAsync(GuardHost.Envelope(Guid.NewGuid(), message));

        host.Faults.Fired.ShouldBeTrue();
        result.Disposition.ShouldBe(ReceiveDisposition.Acknowledge);
        (await host.InboxAsync()).ShouldHaveSingleItem().Status.ShouldBe(
            InboxMessageStatus.Processed, "acknowledged only once the row is Processed");
        host.Probe.Calls(FirstGuardHandler.HandlerName).ShouldBe(2, "the attempt that never committed is replayed");
        (await host.EffectsAsync()).ShouldHaveSingleItem();
        (await host.OutboxAsync()).ShouldHaveSingleItem("the cascade is not duplicated").EventType
            .ShouldBe(typeof(GuardFollowUp).AssemblyQualifiedName);
    }

    /// <summary>
    /// The same fault after the commit reached the database: the outcome looks unknown, the
    /// verification finds the row Processed, and nothing is replayed.
    /// </summary>
    [DockerRequiredFact]
    public async Task WithARetryingStrategy_AFaultAfterTheCommit_IsVerified_AndNothingIsReplayed()
    {
        await using var db = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
        await using var host = GuardHost.Create(db, handlers: bus => bus.AddHandler<FirstGuardHandler>(), retrying: true, broker: new InMemoryBroker());
        host.Probe.PublishFollowUp = true;
        var message = new GuardEvent(Guid.NewGuid());

        host.Faults.Arm(CommitFaultPoint.AfterCommit);
        var result = await host.ReceiveAsync(GuardHost.Envelope(Guid.NewGuid(), message));

        host.Faults.Fired.ShouldBeTrue();
        result.Disposition.ShouldBe(ReceiveDisposition.Acknowledge);
        (await host.InboxAsync()).ShouldHaveSingleItem().Status.ShouldBe(InboxMessageStatus.Processed);
        host.Probe.Calls(FirstGuardHandler.HandlerName).ShouldBe(1, "the commit happened: a replay would run the handler twice");
        (await host.EffectsAsync()).ShouldHaveSingleItem();
        (await host.OutboxAsync()).ShouldHaveSingleItem();
    }

    private static async Task<List<OutboxMessage>> OutboxOf(StoreDatabase db)
    {
        await using var context = new TestMessagingDbContext(
            new DbContextOptionsBuilder<TestMessagingDbContext>().UseNpgsql(db.ConnectionString).Options);
        return await context.OutboxMessages.AsNoTracking().ToListAsync();
    }

    private static async Task Eventually(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition did not hold in time.");
            }

            await Task.Delay(50);
        }
    }
}
