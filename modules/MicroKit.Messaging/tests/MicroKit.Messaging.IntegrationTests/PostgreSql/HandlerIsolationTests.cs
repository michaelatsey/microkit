using Microsoft.Extensions.Time.Testing;

using Microsoft.Extensions.Logging;

namespace MicroKit.Messaging.IntegrationTests.PostgreSql;

/// <summary>
/// V2 for bus handlers (#188 D6): each handler of a message is isolated by its own inbox row — a
/// failure rolls back only that handler's work, is retried from its row, and never reruns the
/// others; a duplicate is never a failure; a lost claim counts nothing; and a failure is
/// acknowledged only once its row is written. C1: on a retry only a lost claim is a lost lease.
/// </summary>
[Collection(PostgreSqlSuite.Name)]
public sealed class HandlerIsolationTests(PostgreSqlFixture fixture)
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    [DockerRequiredFact]
    public async Task TwoHandlers_OneFailing_TheOtherRunsOnce_AndTheFailingOneIsRetriedToProcessed()
    {
        await using var db = await NewDatabase();
        await using var host = GuardHost.Create(db);
        var message = new GuardEvent(Guid.NewGuid());
        host.Probe.Scripts[SecondGuardHandler.HandlerName] = call =>
            call.Call == 1 ? throw new InvalidOperationException("the second handler fails once") : Task.CompletedTask;

        var first = await host.ReceiveAsync(GuardHost.Envelope(Guid.NewGuid(), message));

        first.Disposition.ShouldBe(ReceiveDisposition.Acknowledge, "the failure is recorded: the store retries it");
        var rows = await host.InboxAsync();
        rows.Single(r => r.ConsumerType.Contains(nameof(FirstGuardHandler), StringComparison.Ordinal))
            .Status.ShouldBe(InboxMessageStatus.Processed);
        var failed = rows.Single(r => r.ConsumerType.Contains(nameof(SecondGuardHandler), StringComparison.Ordinal));
        failed.Status.ShouldBe(InboxMessageStatus.Failed);
        failed.RetryCount.ShouldBe(1);
        (await host.EffectsAsync()).ShouldBe([$"first:{message.Id}"], "the failing handler's write rolled back");

        var retry = await host.RetryOnceAsync();

        retry.Processed.ShouldBe(1);
        (await host.InboxAsync()).ShouldAllBe(r => r.Status == InboxMessageStatus.Processed);
        host.Probe.Calls(FirstGuardHandler.HandlerName).ShouldBe(1, "the handler that succeeded never reruns");
        host.Probe.Calls(SecondGuardHandler.HandlerName).ShouldBe(2);
        (await host.EffectsAsync()).ShouldBe([$"first:{message.Id}", $"second:{message.Id}"], ignoreOrder: true);
    }

    [DockerRequiredFact]
    public async Task ARedeliveryWhileTheRowIsFailed_IsSkipped_AndTheRetryRunsOnce()
    {
        await using var db = await NewDatabase();
        await using var host = GuardHost.Create(db, handlers: bus => bus.AddHandler<SecondGuardHandler>());
        var message = new GuardEvent(Guid.NewGuid());
        var envelope = GuardHost.Envelope(Guid.NewGuid(), message);
        host.Probe.Scripts[SecondGuardHandler.HandlerName] = call =>
            call.Call == 1 ? throw new InvalidOperationException("fails once") : Task.CompletedTask;

        await host.ReceiveAsync(envelope);
        var redelivery = await host.ReceiveAsync(envelope);

        redelivery.Disposition.ShouldBe(ReceiveDisposition.Acknowledge);
        host.Probe.Calls(SecondGuardHandler.HandlerName).ShouldBe(1, "a redelivery never starts a second attempt");
        host.Logs.Entries.ShouldContain(e => e.EventId == 2200 && e.Level == LogLevel.Debug);

        await host.RetryOnceAsync();
        await host.RetryOnceAsync();

        host.Probe.Calls(SecondGuardHandler.HandlerName).ShouldBe(2, "the retry ran exactly once");
        (await host.EffectsAsync()).ShouldHaveSingleItem();
    }

    /// <summary>
    /// The handler saves its own context — which flushes the enlisted row — after a concurrent
    /// delivery committed the same row: that save hits the inbox key, and the delivery is a
    /// duplicate (2201), not a failure: no Failed row, nothing counted, the effect once.
    /// </summary>
    [DockerRequiredFact]
    public async Task AHandlersOwnSaveHittingTheInboxKey_IsADuplicate_NotAFailure()
    {
        await using var db = await NewDatabase();
        await using var host = GuardHost.Create(db, handlers: bus => bus.AddHandler<FirstGuardHandler>());
        var message = new GuardEvent(Guid.NewGuid());
        var envelope = GuardHost.Envelope(Guid.NewGuid(), message);

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Probe.Scripts[FirstGuardHandler.HandlerName] = async call =>
        {
            if (call.Call == 1)
            {
                entered.TrySetResult();
                await release.Task;
                await call.Context.SaveChangesAsync(call.Ct);
            }
        };

        var gated = Task.Run(async () => await host.ReceiveAsync(envelope));
        await entered.Task.WaitAsync(Wait);
        (await host.ReceiveAsync(envelope)).Disposition.ShouldBe(ReceiveDisposition.Acknowledge, "the other delivery commits first");
        release.SetResult();
        var late = await gated;

        late.Disposition.ShouldBe(ReceiveDisposition.Acknowledge);
        host.Logs.Entries.ShouldContain(e => e.EventId == 2201);
        host.Logs.Entries.ShouldNotContain(e => e.EventId == 2202 || e.EventId == 2208);
        var row = (await host.InboxAsync()).ShouldHaveSingleItem();
        row.Status.ShouldBe(InboxMessageStatus.Processed);
        row.RetryCount.ShouldBe(0);
        (await host.EffectsAsync()).ShouldHaveSingleItem();
    }

    [DockerRequiredFact]
    public async Task AHandlerThatSavesPublishesThenThrows_CommitsNothing_AndItsRowIsFailed()
    {
        await using var db = await NewDatabase();
        await using var host = GuardHost.Create(db, handlers: bus => bus.AddHandler<FirstGuardHandler>(), broker: new InMemoryBroker());
        host.Probe.PublishFollowUp = true;
        host.Probe.Scripts[FirstGuardHandler.HandlerName] = async call =>
        {
            await call.Context.SaveChangesAsync(call.Ct);
            throw new InvalidOperationException("after its own save and its publication");
        };

        var result = await host.ReceiveAsync(GuardHost.Envelope(Guid.NewGuid(), new GuardEvent(Guid.NewGuid())));

        result.Disposition.ShouldBe(ReceiveDisposition.Acknowledge);
        (await host.EffectsAsync()).ShouldBeEmpty("what it saved rolled back");
        (await host.OutboxAsync()).ShouldBeEmpty("what it published rolled back");
        (await host.InboxAsync()).ShouldHaveSingleItem().Status.ShouldBe(InboxMessageStatus.Failed);
    }

    [DockerRequiredFact]
    public async Task AHandlerThatSavesThenPublishes_CommitsItsDataItsPublicationAndItsRowTogether()
    {
        await using var db = await NewDatabase();
        await using var host = GuardHost.Create(db, handlers: bus => bus.AddHandler<FirstGuardHandler>(), broker: new InMemoryBroker());
        host.Probe.PublishFollowUp = true;
        host.Probe.Scripts[FirstGuardHandler.HandlerName] = call => call.Context.SaveChangesAsync(call.Ct);

        var result = await host.ReceiveAsync(GuardHost.Envelope(Guid.NewGuid(), new GuardEvent(Guid.NewGuid())));

        result.Disposition.ShouldBe(ReceiveDisposition.Acknowledge);
        (await host.EffectsAsync()).ShouldHaveSingleItem();
        (await host.OutboxAsync()).ShouldHaveSingleItem();
        (await host.InboxAsync()).ShouldHaveSingleItem().Status.ShouldBe(InboxMessageStatus.Processed);
    }

    [DockerRequiredFact]
    public async Task ARetryWhoseClaimIsLostMidHandler_RecordsNoFailure_AndLeavesTheAttemptCount()
    {
        await using var db = await NewDatabase();
        await using var host = GuardHost.Create(db, handlers: bus => bus.AddHandler<FirstGuardHandler>());
        var seeded = await host.SeedFailedAsync<FirstGuardHandler>(Guid.NewGuid(), new GuardEvent(Guid.NewGuid()), retryCount: 2);
        var thief = Guid.NewGuid();
        host.Probe.Scripts[FirstGuardHandler.HandlerName] = async _ =>
        {
            // Another loop takes the row over while this attempt runs.
            await using var other = host.NewContext();
            await other.InboxMessages.Where(r => r.RowId == seeded.RowId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.ClaimToken, thief));
        };

        var result = await host.RetryOnceAsync();

        result.LeasesLost.ShouldBe(1);
        result.Retried.ShouldBe(0, "a lost claim is no failure");
        result.DeadLettered.ShouldBe(0);
        var row = (await host.InboxAsync()).ShouldHaveSingleItem();
        row.RetryCount.ShouldBe(2, "the attempt count is unchanged");
        row.ClaimToken.ShouldBe(thief, "the row is the other loop's, untouched");
        (await host.EffectsAsync()).ShouldBeEmpty();
    }

    /// <summary>
    /// A retried handler that saves, then clears the change tracker, has already written its row
    /// Processed inside the attempt's transaction. The commit must recognise that write as its own,
    /// by the <c>ProcessedAtUtc</c> it stamped, and not stage the row again. The clock carries
    /// sub-microsecond ticks, which PostgreSQL does not store, so the stamp read back differs from
    /// the one in memory: only a comparison made in SQL, against the parameter, still matches.
    /// </summary>
    [DockerRequiredFact]
    public async Task ARetriedHandlerThatSavesThenClearsTheTracker_EndsProcessedOnce_WithNoLostLease()
    {
        await using var db = await NewDatabase();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero).AddTicks(7));
        await using var host = GuardHost.Create(db, handlers: bus => bus.AddHandler<FirstGuardHandler>(), clock: clock);
        var message = new GuardEvent(Guid.NewGuid());
        await host.SeedFailedAsync<FirstGuardHandler>(Guid.NewGuid(), message, retryCount: 1);
        host.Probe.Scripts[FirstGuardHandler.HandlerName] = async call =>
        {
            await call.Context.SaveChangesAsync(call.Ct);
            call.Context.ChangeTracker.Clear();
        };

        var result = await host.RetryOnceAsync();

        result.Processed.ShouldBe(1);
        result.LeasesLost.ShouldBe(0, "the commit recognised the row the handler's save had written");
        result.Retried.ShouldBe(0);
        host.Probe.Calls(FirstGuardHandler.HandlerName).ShouldBe(1);
        (await host.EffectsAsync()).ShouldBe([$"first:{message.Id}"]);
        var row = (await host.InboxAsync()).ShouldHaveSingleItem();
        row.Status.ShouldBe(InboxMessageStatus.Processed);
        row.RetryCount.ShouldBe(1, "the attempt count is unchanged");
        row.ClaimToken.ShouldBeNull();
        host.Logs.Entries.ShouldNotContain(e => e.EventId == 2205);
    }

    [DockerRequiredFact]
    public async Task AFailedRowThatCannotBeWritten_LeavesTheDeliveryUnacknowledged_AndItIsRedelivered()
    {
        await using var db = await NewDatabase();
        await using var host = GuardHost.Create(db, handlers: bus => bus.AddHandler<FirstGuardHandler>());
        var envelope = GuardHost.Envelope(Guid.NewGuid(), new GuardEvent(Guid.NewGuid()));
        host.Probe.Scripts[FirstGuardHandler.HandlerName] = _ => throw new InvalidOperationException("fails");

        host.FailedRowInserts.Armed = true;
        var first = await host.ReceiveAsync(envelope);

        first.Disposition.ShouldBe(ReceiveDisposition.Redeliver, "nothing records the failure");
        (await host.InboxAsync()).ShouldBeEmpty();
        host.Logs.Entries.ShouldContain(e => e.EventId == 2204 && e.Level == LogLevel.Error);

        host.FailedRowInserts.Armed = false;
        var redelivered = await host.ReceiveAsync(envelope);

        redelivered.Disposition.ShouldBe(ReceiveDisposition.Acknowledge);
        host.Probe.Calls(FirstGuardHandler.HandlerName).ShouldBe(2, "the redelivery ran the handler again");
        (await host.InboxAsync()).ShouldHaveSingleItem().Status.ShouldBe(InboxMessageStatus.Failed);
    }

    /// <summary>
    /// C1: a retried handler whose own write hits a business unique key fails like any handler: an
    /// attempt is counted on each retry, and the row dead-letters once out of attempts. The row
    /// exists on every retry, so "the row is there" can never mean a duplicate here.
    /// </summary>
    [DockerRequiredFact]
    public async Task ARetriedHandlerHittingABusinessUniqueKey_CountsEachAttempt_AndDeadLettersAfterTheMax()
    {
        await using var db = await NewDatabase();
        await using var host = GuardHost.Create(db, handlers: bus => bus.AddHandler<FirstGuardHandler>(), maxRetries: 3);
        var taken = Guid.NewGuid();
        await using (var seed = host.NewContext())
        {
            seed.BusinessRecords.Add(new BusinessRecord { Id = taken, Value = "already there" });
            await seed.SaveChangesAsync();
        }

        await host.SeedFailedAsync<FirstGuardHandler>(Guid.NewGuid(), new GuardEvent(Guid.NewGuid()), retryCount: 1);
        host.Probe.Scripts[FirstGuardHandler.HandlerName] = call =>
        {
            call.Context.BusinessRecords.Add(new BusinessRecord { Id = taken, Value = "collides" });
            return Task.CompletedTask;
        };

        var second = await host.RetryOnceAsync();
        second.Retried.ShouldBe(1);
        second.LeasesLost.ShouldBe(0, "only a lost claim is a lost lease (C1)");
        (await host.InboxAsync()).Single().RetryCount.ShouldBe(2);

        var third = await host.RetryOnceAsync();
        third.DeadLettered.ShouldBe(1);

        var row = (await host.InboxAsync()).Single();
        row.DeadLettered.ShouldBeTrue();
        row.RetryCount.ShouldBe(3);
        row.Status.ShouldBe(InboxMessageStatus.Failed);
        (await host.EffectsAsync()).ShouldBe(["already there"]);
    }

    /// <summary>
    /// Under a retrying strategy (<c>EnableRetryOnFailure</c>) a failed retry is still settled: the
    /// settlement runs inside the strategy, which refuses a transaction begun outside it. Unsettled,
    /// the row would be re-claimed on every lease expiry with no attempt counted, never dead-lettered.
    /// </summary>
    [DockerRequiredFact]
    public async Task WithARetryingStrategy_AFailedRetryIsSettled_AndItsAttemptCounted()
    {
        await using var db = await NewDatabase();
        await using var host = GuardHost.Create(db, handlers: bus => bus.AddHandler<FirstGuardHandler>(), retrying: true);
        await host.SeedFailedAsync<FirstGuardHandler>(Guid.NewGuid(), new GuardEvent(Guid.NewGuid()), retryCount: 1);
        host.Probe.Scripts[FirstGuardHandler.HandlerName] = _ => throw new InvalidOperationException("fails");

        var result = await host.RetryOnceAsync();

        result.Retried.ShouldBe(1);
        var row = (await host.InboxAsync()).ShouldHaveSingleItem();
        row.RetryCount.ShouldBe(2, "the settlement was written");
        row.Status.ShouldBe(InboxMessageStatus.Failed);
        row.ClaimToken.ShouldBeNull("released to the next pass, not left claimed");
        host.Logs.Entries.ShouldNotContain(e => e.EventId == 2011, "no settlement failure");
    }

    /// <summary>
    /// The source is the producer's, copied from the wire, and may be any length: a capped column
    /// would fail the row on every delivery — never written, so never dead-lettered either.
    /// </summary>
    [DockerRequiredFact]
    public async Task AProducerSourceOfAnyLength_IsRecordedOnTheRow()
    {
        await using var db = await NewDatabase();
        await using var host = GuardHost.Create(db, handlers: bus => bus.AddHandler<FirstGuardHandler>());
        var source = "https://producer.example/" + new string('s', 400);

        var result = await host.ReceiveAsync(GuardHost.Envelope(Guid.NewGuid(), new GuardEvent(Guid.NewGuid()), source));

        result.Disposition.ShouldBe(ReceiveDisposition.Acknowledge);
        var row = (await host.InboxAsync()).ShouldHaveSingleItem();
        row.Status.ShouldBe(InboxMessageStatus.Processed);
        row.Source.ShouldBe(source);
    }

    private Task<StoreDatabase> NewDatabase()
        => StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
}
