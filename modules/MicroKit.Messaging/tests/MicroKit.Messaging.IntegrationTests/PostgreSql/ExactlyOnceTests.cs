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
    public async Task ARetryAbandonedWithItsClaim_IsRecoveredAfterTheLease_AndTheEffectAppliesOnce()
    {
        await using var db = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
        var lease = TimeSpan.FromMilliseconds(300);
        await using var hostA = GuardHost.Create(db, handlers: bus => bus.AddHandler<FirstGuardHandler>(), lease: lease);
        await using var hostB = GuardHost.Create(db, handlers: bus => bus.AddHandler<FirstGuardHandler>());
        var message = new GuardEvent(Guid.NewGuid());
        await hostA.SeedFailedAsync<FirstGuardHandler>(Guid.NewGuid(), message, retryCount: 1);

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hostA.Probe.Scripts[FirstGuardHandler.HandlerName] = async _ =>
        {
            entered.TrySetResult();
            await release.Task;
        };

        var abandoned = hostA.RetryOnceAsync();
        await entered.Task.WaitAsync(Wait);
        await Task.Delay(lease * 2);

        var recovered = await hostB.RetryOnceAsync();
        recovered.Processed.ShouldBe(1, "the expired claim is recovered");

        release.SetResult();
        var late = await abandoned;

        late.LeasesLost.ShouldBe(1, "the abandoned attempt's claim token no longer matches");
        late.Retried.ShouldBe(0);
        (await hostB.EffectsAsync()).ShouldHaveSingleItem().ShouldBe($"first:{message.Id}");
        var row = (await hostB.InboxAsync()).ShouldHaveSingleItem();
        row.Status.ShouldBe(InboxMessageStatus.Processed);
        row.RetryCount.ShouldBe(1, "a lost claim counts no attempt");
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
