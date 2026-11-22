using MicroKit.Messaging.Bus;
using MicroKit.Messaging.Serialization;
using MicroKit.Messaging.Stores;

namespace MicroKit.Messaging.UnitTests.Processing;

/// <summary>
/// <see cref="InboxProcessor"/> — a store's retry loop (#188): the claim, the guarded retry through
/// the real receive pipeline over <see cref="InMemoryInboxGuard"/>, and the settlement of failures.
/// </summary>
public sealed class InboxProcessorTests
{
    private static InboxProcessorOptions DefaultOptions => new()
    {
        BatchSize = 10,
        PollingInterval = TimeSpan.FromMilliseconds(10),
        LeaseDuration = TimeSpan.FromMinutes(1),
        MaxRetries = 3,
        MaxRetryBackoff = TimeSpan.FromHours(1),
    };

    // ── Claim ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessBatch_WhenNothingClaimable_ReturnsEmptyAndSettlesNothing()
    {
        var store = StoreReturning(InboxClaim.Empty);
        var sut = Build(store, out _, out _);

        var result = await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        result.ShouldBe(InboxBatchResult.Empty);
        await store.DidNotReceive().ApplyOutcomesAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyList<InboxOutcome>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatch_ClaimsWithTheConfiguredLeaseDuration()
    {
        var store = StoreReturning(InboxClaim.Empty);
        var options = DefaultOptions;
        options.LeaseDuration = TimeSpan.FromSeconds(97);

        var sut = Build(store, out _, out _, options);
        await sut.ProcessBatchAsync(batchSize: 7, CancellationToken.None);

        await store.Received(1).ClaimBatchAsync(7, TimeSpan.FromSeconds(97), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A tenant in back-off on the store's inbox is left out of the claim (#124 A7); with no tenant in
    /// back-off the list is empty.
    /// </summary>
    [Fact]
    public async Task ProcessBatch_LeavesATenantInBackOffOutOfTheClaim()
    {
        var lists = new List<IReadOnlyList<string>>();
        var store = StoreReturning(InboxClaim.Empty);
        store.ClaimBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                lists.Add(ci.ArgAt<IReadOnlyList<string>>(2));
                return ValueTask.FromResult(InboxClaim.Empty);
            });
        var sut = Build(store, out _, out var harness);

        await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);
        harness.TenantBackoff.For(GuardHarness.Store).OnDown("tenant-b", harness.Clock.GetUtcNow(), out _);
        await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        lists.Count.ShouldBe(2);
        lists[0].ShouldBeEmpty("control: nobody in back-off");
        lists[1].ShouldBe(["tenant-b"]);
    }

    // ── Success: the row is written Processed in the handler's transaction ───────────────────

    [Fact]
    public async Task ProcessBatch_WhenTheRetrySucceeds_MarksTheRowProcessedInItsTransaction_AndWritesNoOutcome()
    {
        var (store, claim, row) = ClaimOne();
        var sut = Build(store, out var probe, out var harness);
        harness.Database.Add(row);

        var result = await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        probe.Calls.ShouldBe(1);
        result.Processed.ShouldBe(1);
        harness.Database.Find(Key(row))!.Status.ShouldBe(InboxMessageStatus.Processed);
        harness.Database.AttemptsBegun.ShouldBe(1);
        await store.DidNotReceive().ApplyOutcomesAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyList<InboxOutcome>>(), Arg.Any<CancellationToken>());
        claim.Token.ShouldNotBe(Guid.Empty);
    }

    // ── Lost claim ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessBatch_WhenTheClaimIsLostBeforeTheHandler_RunsNothingAndWritesNothing()
    {
        var (store, _, row) = ClaimOne();
        var sut = Build(store, out var probe, out var harness);
        harness.Database.Add(Copy(row, claimToken: Guid.NewGuid()));

        var result = await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        result.LeasesLost.ShouldBe(1);
        probe.Calls.ShouldBe(0, "another loop owns the row");
        await store.DidNotReceive().ApplyOutcomesAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyList<InboxOutcome>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatch_WhenTheClaimIsLostMidHandler_RecordsNoFailureAndCountsNoAttempt()
    {
        var (store, _, row) = ClaimOne(retryCount: 1);
        var sut = Build(store, out var probe, out var harness);
        harness.Database.Add(row);
        probe.During = () => harness.Database.Find(Key(row))!.ClaimToken = Guid.NewGuid();

        var result = await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        result.LeasesLost.ShouldBe(1);
        result.Retried.ShouldBe(0);
        result.DeadLettered.ShouldBe(0);
        harness.Database.Find(Key(row))!.RetryCount.ShouldBe(1, "the attempt count is unchanged");
        await store.DidNotReceive().ApplyOutcomesAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyList<InboxOutcome>>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// C1: on a retry the row exists by construction, so "the row is there" proves nothing. A
    /// handler's own conflict — a business unique violation — is the handler failing: an attempt is
    /// counted, and it dead-letters once out of attempts.
    /// </summary>
    [Fact]
    public async Task ProcessBatch_WhenARetriedHandlerHitsABusinessUniqueViolation_CountsTheAttempt()
    {
        var (store, _, row) = ClaimOne(retryCount: 0);
        var sut = Build(store, out var probe, out var harness);
        harness.Database.Add(row);
        probe.Throw = new DuplicateKeyTestException();

        var result = await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        result.LeasesLost.ShouldBe(0, "only a lost claim is a lost lease");
        result.Retried.ShouldBe(1);
        (await CapturedOutcomes(store)).ShouldHaveSingleItem().RetryCount.ShouldBe(1);
    }

    [Fact]
    public async Task ProcessBatch_WhenARetriedHandlerKeepsHittingABusinessUniqueViolation_DeadLettersAfterTheMax()
    {
        var (store, _, row) = ClaimOne(retryCount: DefaultOptions.MaxRetries - 1);
        var sut = Build(store, out var probe, out var harness);
        harness.Database.Add(row);
        probe.Throw = new DuplicateKeyTestException();

        var result = await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        result.DeadLettered.ShouldBe(1);
        var outcome = (await CapturedOutcomes(store)).ShouldHaveSingleItem();
        outcome.Kind.ShouldBe(InboxOutcomeKind.DeadLetter);
        outcome.RetryCount.ShouldBe(DefaultOptions.MaxRetries);
    }

    // ── Permanent, on sight ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessBatch_WhenConsumerNotRegistered_DeadLettersOnFirstSight()
    {
        var (store, _, row) = ClaimOne(consumerType: "Unknown.Consumer, Nowhere");
        var sut = Build(store, out var probe, out _);

        var result = await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        result.DeadLettered.ShouldBe(1);
        probe.Calls.ShouldBe(0);
        var outcome = (await CapturedOutcomes(store)).ShouldHaveSingleItem();
        outcome.Kind.ShouldBe(InboxOutcomeKind.DeadLetter);
        outcome.RetryCount.ShouldBe(row.RetryCount, "a permanent verdict counts no attempt");
    }

    [Fact]
    public async Task ProcessBatch_WhenPayloadDoesNotDeserialize_DeadLettersOnFirstSight()
    {
        var (store, _, _) = ClaimOne();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer.Deserialize(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<Type>())
            .Returns(_ => throw new FormatException("not JSON"));
        var sut = Build(store, out var probe, out _, serializer: serializer);

        var result = await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        result.DeadLettered.ShouldBe(1);
        probe.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task ProcessBatch_WhenPayloadDeserializesToNull_DeadLettersOnFirstSight()
    {
        var (store, _, _) = ClaimOne();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer.Deserialize(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<Type>()).Returns((object?)null);
        var sut = Build(store, out var probe, out _, serializer: serializer);

        var result = await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        result.DeadLettered.ShouldBe(1);
        probe.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task ProcessBatch_WhenTheHandlerThrowsPermanentMessageException_DeadLettersOnFirstSight()
    {
        var (store, _, row) = ClaimOne(retryCount: 0);
        var sut = Build(store, out var probe, out var harness);
        harness.Database.Add(row);
        probe.Throw = new PermanentMessageException("never");

        var result = await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        result.DeadLettered.ShouldBe(1);
        result.Retried.ShouldBe(0);
        (await CapturedOutcomes(store)).ShouldHaveSingleItem().RetryCount.ShouldBe(0);
    }

    // ── The handler's context ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessBatch_HandsTheHandlerTheContextOfTheFirstDelivery_RebuiltFromTheRow()
    {
        var correlation = CorrelationId.New();
        var causation = CausationId.New();
        var (store, _, row) = ClaimOne(tenantId: "tenant-z", correlationId: correlation);
        row.CausationId = causation;
        row.OccurredOnUtc = new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);
        row.Source = "/partner/billing";
        row.Headers = new Dictionary<string, string> { ["x-origin"] = "checkout" };
        var sut = Build(store, out var probe, out var harness);
        harness.Database.Add(row);

        await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        var context = probe.Contexts.ShouldHaveSingleItem();
        context.MessageId.ShouldBe(row.MessageId);
        context.TenantId.ShouldBe("tenant-z");
        context.CorrelationId.ShouldBe(correlation.Value.ToString());
        context.CausationId.ShouldBe(causation.Value.ToString(), "the context COPIES the message's own cause");
        context.OccurredOnUtc.ShouldBe(row.OccurredOnUtc);
        context.Source.ShouldBe("/partner/billing");
        context.Headers["x-origin"].ShouldBe("checkout");
    }

    [Fact]
    public async Task ProcessBatch_WhenTheCorrelationIsNotAGuid_HandsTheRetryTheOneTheFirstDeliveryHad()
    {
        // The column holds a GUID only; the row's headers keep what the envelope carried.
        var (store, _, row) = ClaimOne();
        row.Headers = new Dictionary<string, string> { [MessageHeaders.CorrelationId] = "order-42" };
        var sut = Build(store, out var probe, out var harness);
        harness.Database.Add(row);

        await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        probe.Contexts.ShouldHaveSingleItem().CorrelationId.ShouldBe("order-42");
    }

    [Fact]
    public async Task ProcessBatch_NamesTheDeliveredMessageAsTheCauseOfTheHandlersWork()
    {
        var correlation = CorrelationId.New();
        var (store, _, row) = ClaimOne(correlationId: correlation);
        row.CausationId = CausationId.New(); // an ANCESTOR cause: copying it would name the grandparent
        var sut = Build(store, out _, out var harness);
        harness.Database.Add(row);

        await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        harness.Scopes.Contexts.ShouldNotBeEmpty();
        harness.Scopes.Contexts.ShouldAllBe(c =>
            c.CausationId == row.MessageId.Value.ToString()
            && c.CorrelationId == correlation.Value.ToString()
            && c.TenantId == row.TenantId);
    }

    /// <summary>
    /// C2: every attempt is a fresh scope, and the store of the delivery is written on each one — a
    /// replayed attempt publishes into the same store as the first.
    /// </summary>
    [Fact]
    public async Task ProcessBatch_NamesItsStoreOnEveryAttemptScope_ReplayedAttemptsIncluded()
    {
        var (store, _, row) = ClaimOne();
        var sut = Build(store, out var probe, out var harness);
        harness.Database.Add(row);
        harness.Database.RetryOn = ex => ex is TimeoutException;
        harness.Database.FailNextCommit = new TimeoutException("the commit never reached the database");

        var result = await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        result.Processed.ShouldBe(1);
        probe.Stores.ShouldBe([GuardHarness.Store, GuardHarness.Store], "two attempts, each naming the store");
        probe.ScopeIds.Distinct().Count().ShouldBe(2, "each attempt runs in a scope of its own");
    }

    // ── A commit whose outcome is unknown is verified, never replayed blindly ────────────────

    [Fact]
    public async Task ProcessBatch_WhenTheCommitFailsBeforeReachingTheDatabase_TheStrategyReplaysTheAttempt()
    {
        var (store, _, row) = ClaimOne();
        var sut = Build(store, out var probe, out var harness);
        harness.Database.Add(row);
        harness.Database.RetryOn = ex => ex is TimeoutException;
        harness.Database.FailNextCommit = new TimeoutException("lost before the commit");

        var result = await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        result.Processed.ShouldBe(1);
        probe.Calls.ShouldBe(2, "the verification found no Processed row, so the attempt is replayed");
        harness.Database.Find(Key(row))!.Status.ShouldBe(InboxMessageStatus.Processed);
    }

    [Fact]
    public async Task ProcessBatch_WhenTheCommitFailsAfterReachingTheDatabase_TheVerificationEndsIt()
    {
        var (store, _, row) = ClaimOne();
        var sut = Build(store, out var probe, out var harness);
        harness.Database.Add(row);
        harness.Database.RetryOn = ex => ex is TimeoutException;
        harness.Database.FailAfterNextCommit = new TimeoutException("lost after the commit");

        var result = await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        result.Processed.ShouldBe(1);
        probe.Calls.ShouldBe(1, "the row is Processed: the attempt committed and is not replayed");
    }

    /// <summary>
    /// The store reporting itself unreachable while it verifies an attempt raises the same exception type
    /// a handler raises for its own dependency (F2): it is the store's outage, never one tenant's, so the
    /// batch is abandoned as before #124 and the row's tenant is not put in back-off.
    /// </summary>
    [Fact]
    public async Task ProcessBatch_WhenTheStoreIsUnreachableWhileVerifyingARetry_AbandonsTheBatch_AndBacksOffNoTenant()
    {
        var (store, _, row) = ClaimOne();
        var sut = Build(store, out var probe, out var harness);
        harness.Database.Add(row);
        harness.Database.RetryOn = ex => ex is TimeoutException;
        harness.Database.FailAfterNextCommit = new TimeoutException("lost after the commit");
        probe.During = () => harness.Database.FailReadsWith = new InboxDependencyUnavailableException("the store's database is down");

        var result = await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        row.TenantId.ShouldNotBeNull("control: the row has a tenant to blame");
        result.AbortReason.ShouldBe(InboxBatchAbortReason.DependencyUnavailable);
        harness.TenantBackoff.For(GuardHarness.Store).IsEmpty.ShouldBeTrue("no tenant is blamed for the store's outage");
    }

    /// <summary>A dependency of the row's tenant (#124 D5): that row waits, nothing counted, and the batch goes on.</summary>
    [Fact]
    public async Task ProcessBatch_WhenATenantsDependencyIsUnavailable_DefersThatRow_WithItsAttemptCountUnchanged_AndGoesOn()
    {
        var rows = new[] { Row(retryCount: 2, tenantId: "A"), Row(tenantId: "B") };
        var claim = InboxFixtures.Claim(rows);
        var store = StoreReturning(claim);
        var sut = Build(store, out var probe, out var harness);
        foreach (var row in rows)
        {
            row.ClaimToken = claim.Token;
            harness.Database.Add(row);
        }

        probe.ThrowWhen = context => context.TenantId == "A" ? new InboxDependencyUnavailableException("A's partner is down") : null;

        var result = await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        result.AbortReason.ShouldBe(InboxBatchAbortReason.None);
        result.Processed.ShouldBe(1);
        result.Released.ShouldBe(1, "a tenant's deferral counts no attempt");
        result.Retried.ShouldBe(0);
        var outcome = (await CapturedOutcomes(store)).ShouldHaveSingleItem();
        outcome.Kind.ShouldBe(InboxOutcomeKind.Retry);
        outcome.RetryCount.ShouldBe(2, "unchanged");
        outcome.NextRetryAtUtc.ShouldBe(GuardHarness.Now + DefaultOptions.PollingInterval, "the end of A's back-off");
    }

    /// <summary>
    /// A transport delivery puts the row's tenant in back-off while its retry is in flight (the state is
    /// shared): the retry's own failure is held — it waits for that back-off, and the batch goes on, as
    /// for any tenant's deferral — instead of abandoning the batch (PR #209 review).
    /// </summary>
    [Fact]
    public async Task ProcessBatch_WhenTheTenantEntersBackOffWhileItsRetryIsInFlight_DefersThatRow_AndGoesOn()
    {
        var rows = new[] { Row(retryCount: 2, tenantId: "A"), Row(tenantId: "B") };
        var claim = InboxFixtures.Claim(rows);
        var store = StoreReturning(claim);
        var sut = Build(store, out var probe, out var harness);
        foreach (var row in rows)
        {
            row.ClaimToken = claim.Token;
            harness.Database.Add(row);
        }

        var tenants = harness.TenantBackoff.For(GuardHarness.Store);
        DateTimeOffset blockedUntil = default;
        probe.ThrowWhen = context =>
        {
            if (context.TenantId != "A")
            {
                return null;
            }

            // A delivery of A on a transport thread failed first and put A in back-off.
            tenants.OnDown("A", harness.Clock.GetUtcNow(), out blockedUntil);
            return new InboxDependencyUnavailableException("A's partner is down");
        };

        var result = await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        result.AbortReason.ShouldBe(InboxBatchAbortReason.None, "A's cause is A's own: the batch goes on");
        result.Processed.ShouldBe(1);
        var outcome = (await CapturedOutcomes(store)).ShouldHaveSingleItem();
        outcome.Kind.ShouldBe(InboxOutcomeKind.Retry);
        outcome.RetryCount.ShouldBe(2, "unchanged");
        outcome.NextRetryAtUtc.ShouldBe(blockedUntil, "the back-off the delivery began");
        tenants.TryGetDelay("A", out var delay).ShouldBeTrue();
        delay.ShouldBe(DefaultOptions.PollingInterval, "one failure of the curve");
    }

    // ── Transient failures ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessBatch_WhenHandlerThrows_BelowMaxRetries_SchedulesARetry()
    {
        var (store, _, row) = ClaimOne(retryCount: 0);
        var sut = Build(store, out var probe, out var harness);
        harness.Database.Add(row);
        probe.Throw = new InvalidOperationException("handler error");

        var result = await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        result.Retried.ShouldBe(1);
        var outcome = (await CapturedOutcomes(store)).ShouldHaveSingleItem();
        outcome.Kind.ShouldBe(InboxOutcomeKind.Retry);
        outcome.RetryCount.ShouldBe(1);
        outcome.ErrorMessage!.ShouldContain("handler error");
        outcome.NextRetryAtUtc.ShouldBe(GuardHarness.Now.AddSeconds(2), "no jitter: the delay is the 2^1 s ceiling");
        harness.Database.Find(Key(row))!.Status.ShouldNotBe(InboxMessageStatus.Processed, "the attempt rolled back");
    }

    /// <summary>
    /// A disposed provider while the host keeps running is a failure, not a stop: a stop would
    /// release the row with no attempt charged, and a recurring fault would be re-claimed forever.
    /// </summary>
    [Fact]
    public async Task ProcessBatch_WhenTheProviderIsDisposedWhileTheHostRuns_RetriesTheRowAsAFailure()
    {
        var (store, _, row) = ClaimOne(retryCount: 0);
        var sut = Build(store, out var probe, out var harness);
        harness.Database.Add(row);
        probe.Throw = new ObjectDisposedException(nameof(IServiceProvider));

        var result = await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        result.AbortReason.ShouldBe(InboxBatchAbortReason.None, "the host is running: nothing was stopped");
        result.Retried.ShouldBe(1);
    }

    [Fact]
    public async Task ProcessBatch_WhenHandlerThrows_AtMaxRetries_DeadLetters()
    {
        var (store, _, row) = ClaimOne(retryCount: DefaultOptions.MaxRetries - 1);
        var sut = Build(store, out var probe, out var harness);
        harness.Database.Add(row);
        probe.Throw = new InvalidOperationException("permanent by exhaustion");

        var result = await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        result.DeadLettered.ShouldBe(1);
        (await CapturedOutcomes(store)).ShouldHaveSingleItem().RetryCount.ShouldBe(DefaultOptions.MaxRetries);
    }

    // ── Batch-wide verdicts ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessBatch_WhenDependencyUnavailable_ReleasesTheRemainderWithoutRetries()
    {
        // No tenant to narrow the outage to: the batch is abandoned as before #124.
        var rows = Enumerable.Range(0, 3).Select(_ => Row(tenantId: null)).ToArray();
        var claim = InboxFixtures.Claim(rows);
        var store = StoreReturning(claim);
        var sut = Build(store, out var probe, out var harness);
        foreach (var row in rows)
        {
            row.ClaimToken = claim.Token;
            harness.Database.Add(row);
        }

        probe.Throw = new InboxDependencyUnavailableException("down");

        var result = await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        result.AbortReason.ShouldBe(InboxBatchAbortReason.DependencyUnavailable);
        result.Released.ShouldBe(3, "the failing row and the two never reached");
        result.Retried.ShouldBe(0);
        probe.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task ProcessBatch_WhenTheStoreRegistersNoGuard_ReleasesTheBatchThenRethrows()
    {
        var (store, _, _) = ClaimOne();
        var sut = Build(store, out var probe, out _, registerGuard: false);

        await Should.ThrowAsync<InboxConfigurationException>(
            async () => await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None));

        probe.Calls.ShouldBe(0);
        (await CapturedOutcomes(store)).ShouldHaveSingleItem().Kind.ShouldBe(InboxOutcomeKind.Released);
    }

    [Fact]
    public async Task ProcessBatch_WhenTheGuardCannotBeActivated_RetriesTheRowRatherThanStopping()
    {
        // Registered but unactivatable — one tenant's connection failing — is not a configuration
        // verdict: stopping the loop would halt the inbox for every tenant.
        var (store, _, _) = ClaimOne();
        var sut = Build(store, out _, out _, guardActivationFailure: new InvalidOperationException("no connection"));

        var result = await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        result.Retried.ShouldBe(1);
    }

    [Fact]
    public async Task ProcessBatch_WhenCancelled_ReleasesEveryUnattemptedRow()
    {
        var rows = Enumerable.Range(0, 3).Select(_ => Row()).ToArray();
        var store = StoreReturning(InboxFixtures.Claim(rows));
        var sut = Build(store, out var probe, out _);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await sut.ProcessBatchAsync(batchSize: 10, cts.Token);

        result.AbortReason.ShouldBe(InboxBatchAbortReason.Cancelled);
        result.Released.ShouldBe(3);
        probe.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task ProcessBatch_WhenTheHostStopsMidHandler_ReleasesTheRowWithItsAttemptCountUnchanged()
    {
        var (store, _, row) = ClaimOne(retryCount: 2);
        var sut = Build(store, out var probe, out var harness);
        harness.Database.Add(row);
        using var cts = new CancellationTokenSource();
        probe.During = () =>
        {
            cts.Cancel();
            cts.Token.ThrowIfCancellationRequested();
        };

        var result = await sut.ProcessBatchAsync(batchSize: 10, cts.Token);

        result.AbortReason.ShouldBe(InboxBatchAbortReason.Cancelled);
        result.Retried.ShouldBe(0);
        (await CapturedOutcomes(store)).ShouldHaveSingleItem().Kind.ShouldBe(InboxOutcomeKind.Released);
    }

    // ── Settlement ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessBatch_SettlesOnATokenIndependentOfTheCallersShutdown()
    {
        var (store, _, _) = ClaimOne();
        var sut = Build(store, out _, out _);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await sut.ProcessBatchAsync(batchSize: 10, cts.Token);

        await store.Received(1).ApplyOutcomesAsync(
            Arg.Any<Guid>(),
            Arg.Any<IReadOnlyList<InboxOutcome>>(),
            Arg.Is<CancellationToken>(t => !t.IsCancellationRequested));
    }

    [Fact]
    public async Task ProcessBatch_SettlesWithTheClaimsOwnToken()
    {
        var (store, claim, row) = ClaimOne();
        var sut = Build(store, out var probe, out var harness);
        harness.Database.Add(row);
        probe.Throw = new InvalidOperationException("fails");

        await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        await store.Received(1).ApplyOutcomesAsync(
            claim.Token, Arg.Any<IReadOnlyList<InboxOutcome>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatch_WhenSettlementThrows_DoesNotTakeTheLoopDown()
    {
        var (store, _, row) = ClaimOne();
        store.ApplyOutcomesAsync(
                Arg.Any<Guid>(), Arg.Any<IReadOnlyList<InboxOutcome>>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<int>>(_ => throw new TimeoutException("settlement timed out"));
        var sut = Build(store, out var probe, out var harness);
        harness.Database.Add(row);
        probe.Throw = new InvalidOperationException("fails");

        var result = await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        result.Retried.ShouldBe(1);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────

    private static readonly string ConsumerType = MessageConsumerKey.Of(typeof(RetryHandler));
    private static readonly string EventType = typeof(InboxTestEvent).AssemblyQualifiedName!;

    private static InboxMessageKey Key(InboxMessage row) => new(row.MessageId, row.ConsumerType);

    private static InboxMessage Row(
        int retryCount = 0,
        string? consumerType = null,
        string? tenantId = "tenant-a",
        CorrelationId? correlationId = null)
    {
        var row = InboxFixtures.Message(
            consumerType: consumerType ?? ConsumerType, eventType: EventType, retryCount: retryCount,
            tenantId: tenantId, correlationId: correlationId);
        row.Status = InboxMessageStatus.Processing;
        return row;
    }

    private static InboxMessage Copy(InboxMessage row, Guid claimToken) => new()
    {
        RowId = row.RowId,
        MessageId = row.MessageId,
        ConsumerType = row.ConsumerType,
        EventType = row.EventType,
        Payload = row.Payload,
        Status = row.Status,
        RetryCount = row.RetryCount,
        ClaimToken = claimToken,
    };

    /// <summary>One claimed row: in the claim with its token, ready to be added to the database.</summary>
    private static (IInboxProcessorStore Store, InboxClaim Claim, InboxMessage Row) ClaimOne(
        int retryCount = 0,
        string? consumerType = null,
        string? tenantId = "tenant-a",
        CorrelationId? correlationId = null)
    {
        var row = Row(retryCount, consumerType, tenantId, correlationId);
        var claim = InboxFixtures.Claim(row);
        row.ClaimToken = claim.Token;
        return (StoreReturning(claim), claim, row);
    }

    private static IInboxProcessorStore StoreReturning(InboxClaim claim)
    {
        var store = Substitute.For<IInboxProcessorStore>();
        store.ClaimBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(claim));
        store.ApplyOutcomesAsync(
                Arg.Any<Guid>(), Arg.Any<IReadOnlyList<InboxOutcome>>(), Arg.Any<CancellationToken>())
            .Returns(call => ValueTask.FromResult(call.Arg<IReadOnlyList<InboxOutcome>>().Count));

        return store;
    }

    private static Task<IReadOnlyList<InboxOutcome>> CapturedOutcomes(IInboxProcessorStore store)
    {
        var calls = store.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IInboxProcessorStore.ApplyOutcomesAsync))
            .ToList();

        calls.Count.ShouldBe(1, "the batch settles exactly once");
        return Task.FromResult((IReadOnlyList<InboxOutcome>)calls[0].GetArguments()[1]!);
    }

    private static InboxProcessor Build(
        IInboxProcessorStore store,
        out RetryProbe probe,
        out GuardHarness harness,
        InboxProcessorOptions? options = null,
        IMessageSerializer? serializer = null,
        bool registerGuard = true,
        Exception? guardActivationFailure = null)
    {
        var shared = new RetryProbe();
        probe = shared;
        options ??= DefaultOptions;

        var registry = new MessageHandlerRegistry();
        registry.Register(typeof(InboxTestEvent), ConsumerType, typeof(RetryHandler));
        var contracts = Publishing.IntegrationEventContractFixtures.Registry(_ => { });

        harness = GuardHarness.Build(
            services =>
            {
                services.AddSingleton(shared);
                services.AddScoped<RetryHandler>();
                services.AddScoped<ScopeTag>();
            },
            registry,
            contracts,
            registerGuard: registerGuard,
            guardActivationFailure: guardActivationFailure,
            options: options);

        // The claim's rows are the claim's; in the "database" the test adds what it needs.
        return new InboxProcessor(
            store,
            registry,
            new LocalConsumerRegistry(new MessagingComposition()),
            contracts,
            serializer ?? RoundTripSerializer(),
            harness.Pipeline,
            options,
            harness.Policy,
            NullLogger<InboxProcessor>.Instance,
            GuardHarness.Store,
            harness.TenantBackoff.For(GuardHarness.Store));
    }

    private static IMessageSerializer RoundTripSerializer()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        serializer.Deserialize(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<Type>())
            .Returns(_ => new InboxTestEvent(MessageId.New(), "tenant-a"));

        return serializer;
    }

    /// <summary>What the retried handler saw, and what it does.</summary>
    internal sealed class RetryProbe
    {
        public int Calls { get; set; }

        public List<MessageContext> Contexts { get; } = [];

        public List<MessageStoreKey?> Stores { get; } = [];

        public List<Guid> ScopeIds { get; } = [];

        public Exception? Throw { get; set; }

        /// <summary>Gets or sets a fault chosen from the handler's context: a tenant-scoped one, say.</summary>
        public Func<MessageContext, Exception?>? ThrowWhen { get; set; }

        public Action? During { get; set; }
    }

    /// <summary>One instance per scope, so a test can tell the attempts' scopes apart.</summary>
    internal sealed class ScopeTag
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    internal sealed class RetryHandler(RetryProbe probe, DeliveryStoreHolder holder, ScopeTag scope)
        : IMessageHandler<InboxTestEvent>
    {

        public ValueTask HandleAsync(InboxTestEvent message, MessageContext context, CancellationToken ct = default)
        {
            probe.Calls++;
            probe.Contexts.Add(context);
            probe.Stores.Add(holder.Store);
            probe.ScopeIds.Add(scope.Id);
            probe.During?.Invoke();

            if (probe.Throw is not null)
            {
                throw probe.Throw;
            }

            if (probe.ThrowWhen?.Invoke(context) is { } fault)
            {
                throw fault;
            }

            return ValueTask.CompletedTask;
        }
    }
}
