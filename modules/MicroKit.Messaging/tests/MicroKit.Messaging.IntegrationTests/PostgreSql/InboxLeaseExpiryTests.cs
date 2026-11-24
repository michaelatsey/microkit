using Microsoft.Extensions.Time.Testing;

namespace MicroKit.Messaging.IntegrationTests.PostgreSql;

/// <summary>
/// The transactional settlement guarantee of a retry (#188 D6), against a real database.
/// </summary>
/// <remarks>
/// <para>
/// This is the test that decides whether the ownership mechanism is real or decorative. Without
/// <c>ClaimToken</c> mapped as a concurrency token, the <c>UPDATE</c> that <c>SaveChanges</c>
/// emits carries the primary key alone, the losing processor overwrites the winner, and every
/// other test in the suite still passes.
/// </para>
/// <para>
/// It also proves the claim the whole design rests on: the processed mark and the handler's
/// database side effects commit together or not at all.
/// </para>
/// </remarks>
[Collection(PostgreSqlSuite.Name)]
public sealed class InboxLeaseExpiryTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan ShortLease = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Processor A claims and starts. Its lease expires; processor B claims the row and
    /// completes. A then commits.
    /// </summary>
    [DockerRequiredFact]
    public async Task WhenTheLeaseExpiresMidHandler_TheLoserRollsBackEntirelyAndTheWinnerStands()
    {
        await using var schema = await InboxTestDatabase.CreateAsync(fixture.ConnectionString);
        var rowId = await SeedOneAsync(schema.ConnectionString);

        // ---- Retry A claims, then marks the row and writes a business side effect. ----
        await using var contextA = InboxTestDatabase.NewContext(schema.ConnectionString);
        var storeA = new EfInboxStore<TestMessagingDbContext>(contextA, new FakeTimeProvider(Now));

        var claimA = await storeA.ClaimBatchAsync(10, ShortLease, InboxClaimExclusions.None);
        claimA.Count.ShouldBe(1);

        var key = new InboxMessageKey(
            claimA.Messages[0].MessageId, claimA.Messages[0].ConsumerType);

        (await storeA.MarkClaimedProcessedAsync(key, claimA.Token)).ShouldBeTrue();

        // A's business side effect, written through the same context — which is what makes it
        // share the transaction with the mark.
        var sideEffect = NewSideEffect();
        contextA.OutboxMessages.Add(sideEffect);

        // ---- The lease expires and processor B takes the row over and settles it. ----
        await using var contextB = InboxTestDatabase.NewContext(schema.ConnectionString);
        var storeB = new EfInboxStore<TestMessagingDbContext>(
            contextB, new FakeTimeProvider(Now.Add(ShortLease).AddMinutes(1)));

        var claimB = await storeB.ClaimBatchAsync(10, TimeSpan.FromMinutes(5), InboxClaimExclusions.None);
        claimB.Count.ShouldBe(1, "an expired lease must be re-claimable");
        claimB.Token.ShouldNotBe(claimA.Token);

        (await storeB.ApplyOutcomesAsync(claimB.Token, [InboxOutcome.Processed(key)])).ShouldBe(1);

        // ---- A commits. The concurrency token must reject it. ----
        var thrown = await Should.ThrowAsync<DbUpdateConcurrencyException>(
            async () => await contextA.SaveChangesAsync());

        storeA.IsLeaseLost(thrown).ShouldBeTrue(
            "the processor discriminates a lost lease from a domain conflict by which entity failed");

        await using var probe = InboxTestDatabase.NewContext(schema.ConnectionString);

        // B's outcome stands, untouched by A.
        var row = await probe.InboxMessages.AsNoTracking().SingleAsync(m => m.RowId == rowId);
        row.Status.ShouldBe(InboxMessageStatus.Processed);
        row.ClaimToken.ShouldBeNull();

        // And A's transaction rolled back ENTIRELY — including its business side effect. This is
        // the half that makes the guarantee worth having: a handler whose mark is rejected must
        // not leave its domain writes behind.
        (await probe.OutboxMessages.AsNoTracking().CountAsync(m => m.Id == sideEffect.Id))
            .ShouldBe(0, "the mark and the side effects share one transaction, or neither commits");
    }

    /// <summary>
    /// The nominal half of the same mechanism: while the lease holds, the mark and the side
    /// effects commit together.
    /// </summary>
    [DockerRequiredFact]
    public async Task WhenTheLeaseHolds_TheMarkAndTheSideEffectsCommitTogether()
    {
        await using var schema = await InboxTestDatabase.CreateAsync(fixture.ConnectionString);
        var rowId = await SeedOneAsync(schema.ConnectionString);

        await using var context = InboxTestDatabase.NewContext(schema.ConnectionString);
        var store = new EfInboxStore<TestMessagingDbContext>(context, new FakeTimeProvider(Now));

        var claim = await store.ClaimBatchAsync(10, TimeSpan.FromMinutes(5), InboxClaimExclusions.None);
        var key = new InboxMessageKey(claim.Messages[0].MessageId, claim.Messages[0].ConsumerType);

        await store.BeginAsync();
        (await store.MarkClaimedProcessedAsync(key, claim.Token)).ShouldBeTrue();

        var sideEffect = NewSideEffect();
        context.OutboxMessages.Add(sideEffect);

        await store.CommitAsync();

        await using var probe = InboxTestDatabase.NewContext(schema.ConnectionString);
        var row = await probe.InboxMessages.AsNoTracking().SingleAsync(m => m.RowId == rowId);

        row.Status.ShouldBe(InboxMessageStatus.Processed);
        row.ClaimToken.ShouldBeNull("a committed success clears the token, so no deferred write can undo it");
        (await probe.OutboxMessages.AsNoTracking().CountAsync(m => m.Id == sideEffect.Id)).ShouldBe(1);
    }

    /// <summary>
    /// The corollary the claim token's second job provides: a release issued after the handler
    /// committed cannot undo the success. Nothing has to check for it — it is structurally
    /// impossible, because the committed transaction cleared the token the release filters on.
    /// </summary>
    [DockerRequiredFact]
    public async Task AReleaseIssuedAfterACommittedSuccess_IsANoOp()
    {
        await using var schema = await InboxTestDatabase.CreateAsync(fixture.ConnectionString);
        var rowId = await SeedOneAsync(schema.ConnectionString);

        await using var context = InboxTestDatabase.NewContext(schema.ConnectionString);
        var store = new EfInboxStore<TestMessagingDbContext>(context, new FakeTimeProvider(Now));

        var claim = await store.ClaimBatchAsync(10, TimeSpan.FromMinutes(5), InboxClaimExclusions.None);
        var key = new InboxMessageKey(claim.Messages[0].MessageId, claim.Messages[0].ConsumerType);

        await store.BeginAsync();
        await store.MarkClaimedProcessedAsync(key, claim.Token);
        await store.CommitAsync();

        // The host shuts down and the batch settles everything it did not observe as done.
        var written = await store.ApplyOutcomesAsync(claim.Token, [InboxOutcome.Released(key)]);

        written.ShouldBe(0);

        await using var probe = InboxTestDatabase.NewContext(schema.ConnectionString);
        var row = await probe.InboxMessages.AsNoTracking().SingleAsync(m => m.RowId == rowId);
        row.Status.ShouldBe(InboxMessageStatus.Processed, "a committed success cannot be undone");
    }

    /// <summary>
    /// The Failed row of a new delivery is an INSERT, never an upsert (D6e): a row already there
    /// means another delivery owns the pair, reported as <see langword="false"/> rather than thrown,
    /// and the context stays usable — on PostgreSQL, where a violation aborts the transaction,
    /// which is why the insert runs in no transaction of the caller.
    /// </summary>
    [DockerRequiredFact]
    public async Task TryInsertFailed_WhenTheRowExists_ReportsItAndLeavesTheContextUsable()
    {
        await using var schema = await InboxTestDatabase.CreateAsync(fixture.ConnectionString);

        await using var context = InboxTestDatabase.NewContext(schema.ConnectionString);
        var store = new EfInboxStore<TestMessagingDbContext>(context, new FakeTimeProvider(Now));

        var message = NewMessage("OrderPlacedHandler");
        (await store.TryInsertFailedAsync(message)).ShouldBeTrue();
        (await store.TryInsertFailedAsync(NewMessage(message.ConsumerType, message.MessageId))).ShouldBeFalse();
        (await store.TryInsertFailedAsync(NewMessage("InvoiceIssuedHandler"))).ShouldBeTrue(
            "the duplicate left nothing behind on the context");

        await using var probe = InboxTestDatabase.NewContext(schema.ConnectionString);
        (await probe.InboxMessages.AsNoTracking().CountAsync()).ShouldBe(2);
    }

    private static async Task<Guid> SeedOneAsync(string connectionString)
    {
        await using var context = InboxTestDatabase.NewContext(connectionString);

        var message = NewMessage("OrderPlacedHandler");
        context.InboxMessages.Add(message);
        await context.SaveChangesAsync();

        return message.RowId;
    }

    private static InboxMessage NewMessage(string consumerType, MessageId? messageId = null)
        => new()
        {
            RowId = Guid.NewGuid(),
            MessageId = messageId ?? MessageId.New(),
            ConsumerType = consumerType,
            TenantId = "tenant-a",
            EventType = "MicroKit.Test.TestEvent, MicroKit.Test",
            Payload = "{}",
            Status = InboxMessageStatus.Received,
            ReceivedAtUtc = Now,
        };

    /// <summary>
    /// A business side effect written through the handler's own context. An outbox row is used
    /// only because it is a table the test DbContext already maps — nothing about the outbox is
    /// under test here.
    /// </summary>
    private static OutboxMessage NewSideEffect()
        => new()
        {
            Id = MessageId.New(),
            TenantId = "tenant-a",
            EventType = "MicroKit.Test.SideEffect, MicroKit.Test",
            Payload = "{}",
            Status = OutboxMessageStatus.Pending,
            OccurredOnUtc = Now,
            CreatedAtUtc = Now,
            CorrelationId = CorrelationId.New(),
        };
}
