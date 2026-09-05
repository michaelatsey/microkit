using Microsoft.Extensions.Time.Testing;

using MicroKit.Messaging.Serialization;

namespace MicroKit.Messaging.UnitTests.Processing;

public sealed class InboxProcessorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    private static InboxProcessorOptions DefaultOptions => new()
    {
        BatchSize = 10,
        PollingInterval = TimeSpan.FromMilliseconds(10),
        LeaseDuration = TimeSpan.FromMinutes(1),
        MaxRetries = 3,
        MaxRetryBackoff = TimeSpan.FromHours(1),
    };

    // -----------------------------------------------------------------------------------
    // Claim contract
    // -----------------------------------------------------------------------------------

    [Fact]
    public async Task ProcessBatch_WhenNothingClaimable_ReturnsEmptyAndSettlesNothing()
    {
        var store = Substitute.For<IInboxProcessorStore>();
        store.ClaimBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(InboxClaim.Empty));

        var sut = Build(store, out _, out _);
        var result = await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        result.ShouldBe(InboxBatchResult.Empty);
        await store.DidNotReceive().ApplyOutcomesAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyList<InboxOutcome>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatch_ClaimsWithTheConfiguredLeaseDuration()
    {
        var store = Substitute.For<IInboxProcessorStore>();
        store.ClaimBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(InboxClaim.Empty));

        var options = DefaultOptions;
        options.LeaseDuration = TimeSpan.FromSeconds(97);

        var sut = Build(store, out _, out _, options);
        await sut.ProcessBatchAsync(batchSize: 7, CancellationToken.None);

        await store.Received(1).ClaimBatchAsync(
            7, TimeSpan.FromSeconds(97), Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------------------
    // Success — settled inside the handler's transaction, so no outcome is written
    // -----------------------------------------------------------------------------------

    [Fact]
    public async Task ProcessBatch_WhenHandlerCommitsTheStagedMark_WritesNoOutcome()
    {
        var message = InboxFixtures.Message(consumerType: ConsumerType, eventType: EventType);
        var store = StoreReturning(InboxFixtures.Claim(message));

        var sut = Build(store, out var handler, out var settlement);
        settlement.MarkUncommitted = false;

        var result = await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        handler.InvocationCount.ShouldBe(1);
        settlement.Staged.ShouldBe([InboxFixtures.KeyOf(message)]);
        result.Processed.ShouldBe(1);
        result.Claimed.ShouldBe(1);

        // The whole point of settling inside the handler transaction: nothing to write here.
        await store.DidNotReceive().ApplyOutcomesAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyList<InboxOutcome>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatch_StagesTheMarkBeforeInvokingTheHandler()
    {
        // Staging first is what puts the mark inside whatever unit of work the handler commits.
        // Staging after would reopen the crash window this design exists to close.
        var message = InboxFixtures.Message(consumerType: ConsumerType, eventType: EventType);
        var store = StoreReturning(InboxFixtures.Claim(message));

        var sut = Build(store, out var handler, out var settlement);
        settlement.Owned = false;

        await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        settlement.Staged.Count.ShouldBe(1);
        handler.InvocationCount.ShouldBe(0, "staging reported the lease lost, so the handler must not run");
    }

    // -----------------------------------------------------------------------------------
    // Lease loss
    // -----------------------------------------------------------------------------------

    [Fact]
    public async Task ProcessBatch_WhenLeaseLostBeforeHandler_CountsItAndLeavesTheRowAlone()
    {
        var message = InboxFixtures.Message(consumerType: ConsumerType, eventType: EventType);
        var store = StoreReturning(InboxFixtures.Claim(message));

        var sut = Build(store, out var handler, out var settlement);
        settlement.Owned = false;

        var result = await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        result.LeasesLost.ShouldBe(1);
        result.Processed.ShouldBe(0);
        result.Retried.ShouldBe(0);
        handler.InvocationCount.ShouldBe(0);

        // Writing anything would overwrite the processor that legitimately owns the row now.
        await store.DidNotReceive().ApplyOutcomesAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyList<InboxOutcome>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatch_WhenLeaseLostDuringHandler_CountsItAndConsumesNoRetry()
    {
        var message = InboxFixtures.Message(consumerType: ConsumerType, eventType: EventType);
        var store = StoreReturning(InboxFixtures.Claim(message));
        var conflict = new InvalidOperationException("concurrency token rejected the update");

        var sut = Build(store, out var handler, out var settlement);
        handler.ThrowOnHandle = conflict;
        settlement.LeaseLost = ex => ReferenceEquals(ex, conflict);

        var result = await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        result.LeasesLost.ShouldBe(1);
        result.Retried.ShouldBe(0, "the row is no longer ours to write");
        result.DeadLettered.ShouldBe(0);
        await store.DidNotReceive().ApplyOutcomesAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyList<InboxOutcome>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatch_WhenADomainConflictIsNotALostLease_KeepsItsRetry()
    {
        // A domain concurrency conflict and a lost lease surface from the same SaveChanges as the
        // same exception type. The discrimination is structural — which entity failed — and a
        // domain conflict is legitimately transient.
        var message = InboxFixtures.Message(consumerType: ConsumerType, eventType: EventType);
        var store = StoreReturning(InboxFixtures.Claim(message));

        var sut = Build(store, out var handler, out var settlement);
        handler.ThrowOnHandle = new InvalidOperationException("aggregate version conflict");
        settlement.LeaseLost = _ => false;
        settlement.MarkUncommitted = true;

        var result = await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        result.LeasesLost.ShouldBe(0);
        result.Retried.ShouldBe(1);
        (await CapturedOutcomes(store))[0].Kind.ShouldBe(InboxOutcomeKind.Retry);
    }

    // -----------------------------------------------------------------------------------
    // Permanent failures — dead-lettered on FIRST sight, never after MaxRetries
    // -----------------------------------------------------------------------------------

    [Fact]
    public async Task ProcessBatch_WhenConsumerNotRegistered_DeadLettersOnFirstSight()
    {
        var message = InboxFixtures.Message(
            consumerType: "Unknown.Consumer, NonExistentAssembly", eventType: EventType);
        var store = StoreReturning(InboxFixtures.Claim(message));

        var sut = Build(store, out var handler, out _);
        var result = await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        result.DeadLettered.ShouldBe(1);
        result.Retried.ShouldBe(0, "no number of retries resolves a missing registration");
        handler.InvocationCount.ShouldBe(0);

        var outcomes = await CapturedOutcomes(store);
        outcomes[0].Kind.ShouldBe(InboxOutcomeKind.DeadLetter);
        outcomes[0].RetryCount.ShouldBe(0, "the retry budget was never spent");
    }

    [Fact]
    public async Task ProcessBatch_WhenPayloadDoesNotDeserialize_DeadLettersOnFirstSight()
    {
        var message = InboxFixtures.Message(
            consumerType: ConsumerType, eventType: EventType, payload: "{ not json");
        var store = StoreReturning(InboxFixtures.Claim(message));

        var serializer = Substitute.For<IMessageSerializer>();
        serializer.Deserialize(Arg.Any<string>(), Arg.Any<string>())
            .Returns(_ => throw new InvalidOperationException("malformed JSON"));

        var sut = Build(store, out var handler, out _, serializer: serializer);
        var result = await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        result.DeadLettered.ShouldBe(1);
        result.Retried.ShouldBe(0);
        handler.InvocationCount.ShouldBe(0);
    }

    [Fact]
    public async Task ProcessBatch_WhenPayloadDeserializesToNull_DeadLettersOnFirstSight()
    {
        var message = InboxFixtures.Message(consumerType: ConsumerType, eventType: EventType);
        var store = StoreReturning(InboxFixtures.Claim(message));

        var serializer = Substitute.For<IMessageSerializer>();
        serializer.Deserialize(Arg.Any<string>(), Arg.Any<string>()).Returns((object?)null);

        var sut = Build(store, out _, out _, serializer: serializer);
        var result = await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        result.DeadLettered.ShouldBe(1);
        result.Retried.ShouldBe(0);
    }

    // -----------------------------------------------------------------------------------
    // Transient failures and the retry ceiling
    // -----------------------------------------------------------------------------------

    [Fact]
    public async Task ProcessBatch_WhenHandlerThrows_BelowMaxRetries_SchedulesARetry()
    {
        var message = InboxFixtures.Message(
            consumerType: ConsumerType, eventType: EventType, retryCount: 0);
        var store = StoreReturning(InboxFixtures.Claim(message));

        var sut = Build(store, out var handler, out var settlement);
        handler.ThrowOnHandle = new InvalidOperationException("handler error");
        settlement.MarkUncommitted = true;

        var result = await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        result.Retried.ShouldBe(1);
        result.DeadLettered.ShouldBe(0);

        var outcome = (await CapturedOutcomes(store))[0];
        outcome.Kind.ShouldBe(InboxOutcomeKind.Retry);
        outcome.RetryCount.ShouldBe(1);
        outcome.ErrorMessage.ShouldNotBeNull().ShouldContain("handler error");

        // FixedRandom.NoJitter neutralises the jitter, so the delay IS the ceiling: 2^1 = 2 s.
        outcome.NextRetryAtUtc.ShouldBe(Now.AddSeconds(2));
    }

    [Fact]
    public async Task ProcessBatch_WhenHandlerThrows_AtMaxRetries_DeadLetters()
    {
        var message = InboxFixtures.Message(
            consumerType: ConsumerType, eventType: EventType,
            retryCount: DefaultOptions.MaxRetries - 1);
        var store = StoreReturning(InboxFixtures.Claim(message));

        var sut = Build(store, out var handler, out var settlement);
        handler.ThrowOnHandle = new InvalidOperationException("permanent by exhaustion");
        settlement.MarkUncommitted = true;

        var result = await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        result.DeadLettered.ShouldBe(1);
        result.Retried.ShouldBe(0);

        var outcome = (await CapturedOutcomes(store))[0];
        outcome.Kind.ShouldBe(InboxOutcomeKind.DeadLetter);
        outcome.RetryCount.ShouldBe(DefaultOptions.MaxRetries);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(5, 32)]
    [InlineData(10, 1024)]
    public void ComputeBackoffCeiling_FollowsTheExponentialCurve(int retryCount, int expectedSeconds)
    {
        var sut = Build(Substitute.For<IInboxProcessorStore>(), out _, out _);

        sut.ComputeBackoffCeiling(retryCount).ShouldBe(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public void ComputeBackoffCeiling_IsCappedAtMaxRetryBackoff()
    {
        var sut = Build(Substitute.For<IInboxProcessorStore>(), out _, out _);

        sut.ComputeBackoffCeiling(20).ShouldBe(TimeSpan.FromHours(1));
    }

    [Fact]
    public void ComputeBackoffCeiling_ClampsACorruptedRetryCount()
    {
        // The exponent is clamped before shifting, so a nonsense retry count cannot overflow.
        var sut = Build(Substitute.For<IInboxProcessorStore>(), out _, out _);

        Should.NotThrow(() => sut.ComputeBackoffCeiling(int.MaxValue));
        sut.ComputeBackoffCeiling(int.MaxValue).ShouldBe(TimeSpan.FromHours(1));
        sut.ComputeBackoffCeiling(-5).ShouldBe(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ApplyJitter_DrawsOverTheWholeInterval()
    {
        var zero = Build(
            Substitute.For<IInboxProcessorStore>(), out _, out _, random: FixedRandom.ZeroDelay);
        var full = Build(
            Substitute.For<IInboxProcessorStore>(), out _, out _, random: FixedRandom.NoJitter);

        zero.ApplyJitter(TimeSpan.FromSeconds(60)).ShouldBe(TimeSpan.Zero);
        full.ApplyJitter(TimeSpan.FromSeconds(60)).ShouldBe(TimeSpan.FromSeconds(60));
        full.ApplyJitter(TimeSpan.Zero).ShouldBe(TimeSpan.Zero);
    }

    // -----------------------------------------------------------------------------------
    // Handler that commits nothing — detected, not degraded silently
    // -----------------------------------------------------------------------------------

    [Fact]
    public async Task ProcessBatch_WhenHandlerCommitsNothing_WritesADeferredProcessedOutcome()
    {
        // Without this the staged mark is never persisted: the row replays on every pass and
        // eventually dead-letters although every invocation succeeded.
        var message = InboxFixtures.Message(consumerType: ConsumerType, eventType: EventType);
        var store = StoreReturning(InboxFixtures.Claim(message));

        var sut = Build(store, out var handler, out var settlement);
        settlement.MarkUncommitted = true;

        var result = await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        handler.InvocationCount.ShouldBe(1);
        result.Processed.ShouldBe(1);
        result.Retried.ShouldBe(0);

        var outcome = (await CapturedOutcomes(store))[0];
        outcome.Kind.ShouldBe(InboxOutcomeKind.Processed);
    }

    /// <summary>
    /// A handler that commits and then throws has done its work: the row is durably Processed.
    /// Retrying would be wrong in principle and a no-op in practice, because the committed
    /// transaction cleared the claim token.
    /// </summary>
    [Fact]
    public async Task ProcessBatch_WhenHandlerCommitsThenThrows_CountsItProcessedAndDoesNotRetry()
    {
        var message = InboxFixtures.Message(consumerType: ConsumerType, eventType: EventType);
        var store = StoreReturning(InboxFixtures.Claim(message));

        var sut = Build(store, out var handler, out var settlement);
        handler.ThrowOnHandle = new InvalidOperationException("thrown after commit");
        settlement.MarkUncommitted = false;

        var result = await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        result.Processed.ShouldBe(1);
        result.Retried.ShouldBe(0);
        result.DeadLettered.ShouldBe(0);
        await store.DidNotReceive().ApplyOutcomesAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyList<InboxOutcome>>(), Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------------------
    // Batch-wide aborts
    // -----------------------------------------------------------------------------------

    [Fact]
    public async Task ProcessBatch_WhenDependencyUnavailable_ReleasesTheRemainderWithoutRetries()
    {
        // The failure mode this prevents: an outage failing all N rows, writing N failure rows,
        // and consuming N retry budgets — then repeating next tick.
        var messages = Enumerable.Range(0, 4)
            .Select(_ => InboxFixtures.Message(consumerType: ConsumerType, eventType: EventType))
            .ToArray();
        var store = StoreReturning(InboxFixtures.Claim(messages));

        var sut = Build(store, out var handler, out var settlement);
        handler.ThrowOnHandle = new InboxDependencyUnavailableException("payments API is down");
        settlement.MarkUncommitted = true;

        var result = await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        result.AbortReason.ShouldBe(InboxBatchAbortReason.DependencyUnavailable);
        result.Released.ShouldBe(4, "including the row that triggered the abort");
        result.Retried.ShouldBe(0);
        result.DeadLettered.ShouldBe(0);
        handler.InvocationCount.ShouldBe(1, "the batch is abandoned, not attempted row by row");

        var outcomes = await CapturedOutcomes(store);
        outcomes.ShouldAllBe(o => o.Kind == InboxOutcomeKind.Released);
    }

    [Fact]
    public async Task ProcessBatch_WhenSettlementStoreMissing_SettlesReleasedThenRethrows()
    {
        // A missing registration is a deployment defect. The batch must be settled first, so the
        // worker can stop without stranding a single lease.
        var messages = Enumerable.Range(0, 3)
            .Select(_ => InboxFixtures.Message(consumerType: ConsumerType, eventType: EventType))
            .ToArray();
        var store = StoreReturning(InboxFixtures.Claim(messages));

        var sut = Build(store, out _, out _, registerSettlementStore: false);

        await Should.ThrowAsync<InboxConfigurationException>(
            async () => await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None));

        var outcomes = await CapturedOutcomes(store);
        outcomes.Count.ShouldBe(3);
        outcomes.ShouldAllBe(o => o.Kind == InboxOutcomeKind.Released);
    }

    [Fact]
    public async Task ProcessBatch_WhenHandlerNotResolvable_SettlesReleasedThenRethrows()
    {
        var message = InboxFixtures.Message(consumerType: ConsumerType, eventType: EventType);
        var store = StoreReturning(InboxFixtures.Claim(message));

        var sut = Build(store, out _, out _, registerHandler: false);

        await Should.ThrowAsync<InboxConfigurationException>(
            async () => await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None));

        (await CapturedOutcomes(store)).ShouldAllBe(o => o.Kind == InboxOutcomeKind.Released);
    }

    [Fact]
    public async Task ProcessBatch_WhenCancelled_ReleasesEveryUnattemptedRow()
    {
        var messages = Enumerable.Range(0, 3)
            .Select(_ => InboxFixtures.Message(consumerType: ConsumerType, eventType: EventType))
            .ToArray();
        var store = StoreReturning(InboxFixtures.Claim(messages));

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var sut = Build(store, out var handler, out _);
        var result = await sut.ProcessBatchAsync(batchSize: 10, cts.Token);

        result.AbortReason.ShouldBe(InboxBatchAbortReason.Cancelled);
        result.Released.ShouldBe(3);
        handler.InvocationCount.ShouldBe(0);
    }

    [Fact]
    public async Task ProcessBatch_SettlesOnATokenIndependentOfTheCallersShutdown()
    {
        // If a shutdown cancelled the settlement write, every lease in the batch would stay held
        // until expiry. The flush gets its own short timeout instead.
        var message = InboxFixtures.Message(consumerType: ConsumerType, eventType: EventType);
        var store = StoreReturning(InboxFixtures.Claim(message));

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var sut = Build(store, out _, out _);
        await sut.ProcessBatchAsync(batchSize: 10, cts.Token);

        await store.Received(1).ApplyOutcomesAsync(
            Arg.Any<Guid>(),
            Arg.Any<IReadOnlyList<InboxOutcome>>(),
            Arg.Is<CancellationToken>(t => !t.IsCancellationRequested));
    }

    [Fact]
    public async Task ProcessBatch_SettlesWithTheClaimsOwnToken()
    {
        var message = InboxFixtures.Message(consumerType: ConsumerType, eventType: EventType);
        var claim = InboxFixtures.Claim(message);
        var store = StoreReturning(claim);

        var sut = Build(store, out var handler, out var settlement);
        handler.ThrowOnHandle = new InvalidOperationException("boom");
        settlement.MarkUncommitted = true;

        await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        await store.Received(1).ApplyOutcomesAsync(
            claim.Token, Arg.Any<IReadOnlyList<InboxOutcome>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatch_WhenSettlementThrows_DoesNotTakeTheWorkerDown()
    {
        // Leases expire on their own and the rows are re-claimed. Letting this escape would take
        // down the hosting BackgroundService over a condition the inbox is designed to survive.
        var message = InboxFixtures.Message(consumerType: ConsumerType, eventType: EventType);
        var store = StoreReturning(InboxFixtures.Claim(message));
        store.ApplyOutcomesAsync(
                Arg.Any<Guid>(), Arg.Any<IReadOnlyList<InboxOutcome>>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<int>>(_ => throw new InvalidOperationException("database gone"));

        var sut = Build(store, out var handler, out var settlement);
        handler.ThrowOnHandle = new InvalidOperationException("boom");
        settlement.MarkUncommitted = true;

        var result = default(InboxBatchResult);
        await Should.NotThrowAsync(async () =>
            result = await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None));

        result.Retried.ShouldBe(1);
    }

    // -----------------------------------------------------------------------------------
    // Drain contract — the processor never ingests
    // -----------------------------------------------------------------------------------

    [Fact]
    public void InboxProcessor_DependsOnTheClaimStoreOnly_NotOnTheWriter()
    {
        // Structural, not a convention: the processor cannot call ExistsAsync or AddAsync
        // because IInboxProcessorStore does not expose them.
        var parameterTypes = typeof(InboxProcessor)
            .GetConstructors()[0]
            .GetParameters()
            .Select(p => p.ParameterType)
            .ToList();

        parameterTypes.ShouldContain(typeof(IInboxProcessorStore));
        parameterTypes.ShouldNotContain(typeof(IInboxWriter));
    }

    // -----------------------------------------------------------------------------------
    // The causation link — what makes the delivered message the cause of the handler's work
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Everything the handler stages names the delivered message as its cause, so
    /// <c>CausationId</c> is derived from <c>message.MessageId</c> — never copied from the row's
    /// own <c>CausationId</c>, and never taken from <c>RowId</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>RowId</c> is the local surrogate primary key; it is meaningless outside this table and to
    /// every other process. <c>MessageId</c> is the end-to-end identity the producer assigned, and
    /// is what answers "which message caused this" downstream. The row is seeded so that all three
    /// candidate values differ — otherwise the test passes under every implementation.
    /// </para>
    /// <para>
    /// Reachability note: nothing produces inbox rows in this release (ADR-MSG-019) and
    /// <c>InboxIngestionValidator</c> fails a host that registers a handler, so this path is
    /// exercised only by driving the processor directly, as here. It is asserted anyway — the
    /// receiving seam will arrive against this behaviour, not decide it afresh.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ProcessBatch_NamesTheDeliveredMessageAsTheCauseOfTheHandlersWork()
    {
        var ancestorCause = CausationId.New();
        var message = InboxFixtures.Message(consumerType: ConsumerType, eventType: EventType);
        message.CausationId = ancestorCause;

        var store = StoreReturning(InboxFixtures.Claim(message));
        var sut = BuildRecordingScopes(store, out var scopes);

        await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        var ctx = scopes.Contexts.ShouldHaveSingleItem();

        ctx.CausationId.ShouldBe(
            message.MessageId.Value.ToString(),
            "the cause of the handler's work is the message being delivered");

        ctx.CausationId.ShouldNotBe(
            ancestorCause.Value.ToString(),
            "copying the row's own causation names the grandparent, one hop too far up");

        ctx.CausationId.ShouldNotBe(
            message.RowId.ToString(),
            "RowId is a local surrogate — it names nothing any other process can resolve");
    }

    /// <summary>
    /// The correlation is copied through unchanged — it identifies the chain, not a hop.
    /// </summary>
    /// <remarks>
    /// The mirror of the outbox's <c>ProcessBatch_PropagatesCorrelationUnchangedWhileDerivingCausation</c>,
    /// and the asymmetry is what let this go uncovered: correlation and causation are propagated by
    /// adjacent lines and only a test that seeds the correlation distinctly can tell one from the
    /// other. Every other test in this file leaves <c>CorrelationId</c> null, so nulling the copy
    /// fails none of them.
    /// </remarks>
    [Fact]
    public async Task ProcessBatch_PropagatesCorrelationUnchangedWhileDerivingCausation()
    {
        var correlation = CorrelationId.New();
        var message = InboxFixtures.Message(
            consumerType: ConsumerType, eventType: EventType, correlationId: correlation);

        var store = StoreReturning(InboxFixtures.Claim(message));
        var sut = BuildRecordingScopes(store, out var scopes);

        await sut.ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        var ctx = scopes.Contexts.ShouldHaveSingleItem();

        ctx.CorrelationId.ShouldBe(
            correlation.Value.ToString(),
            "correlation identifies the whole chain and never advances a hop");

        ctx.CausationId.ShouldBe(
            message.MessageId.Value.ToString(),
            "causation advances while correlation does not — asserting one without the other " +
            "passes while the two are confused for each other");

        ctx.TenantId.ShouldBe(message.TenantId);
    }

    // -----------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------

    private static readonly string ConsumerType = typeof(RecordingInboxHandler).AssemblyQualifiedName!;
    private static readonly string EventType = typeof(InboxTestEvent).AssemblyQualifiedName!;

    private static IInboxProcessorStore StoreReturning(InboxClaim claim)
    {
        var store = Substitute.For<IInboxProcessorStore>();
        store.ClaimBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(claim));
        store.ApplyOutcomesAsync(
                Arg.Any<Guid>(), Arg.Any<IReadOnlyList<InboxOutcome>>(), Arg.Any<CancellationToken>())
            .Returns(call => ValueTask.FromResult(call.Arg<IReadOnlyList<InboxOutcome>>().Count));

        return store;
    }

    private static async ValueTask<IReadOnlyList<InboxOutcome>> CapturedOutcomes(
        IInboxProcessorStore store)
    {
        var calls = store.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IInboxProcessorStore.ApplyOutcomesAsync))
            .ToList();

        calls.Count.ShouldBe(1, "the batch settles exactly once");

        return await ValueTask.FromResult((IReadOnlyList<InboxOutcome>)calls[0].GetArguments()[1]!);
    }

    private static InboxProcessor Build(
        IInboxProcessorStore store,
        out RecordingInboxHandler handler,
        out ScriptedInboxSettlementStore settlement,
        InboxProcessorOptions? options = null,
        IMessageSerializer? serializer = null,
        Random? random = null,
        bool registerHandler = true,
        bool registerSettlementStore = true)
    {
        handler = new RecordingInboxHandler();
        settlement = new ScriptedInboxSettlementStore();

        var services = new ServiceCollection();
        if (registerHandler)
        {
            services.AddSingleton(handler);
        }

        if (registerSettlementStore)
        {
            services.AddSingleton<IInboxSettlementStore>(settlement);
        }

        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var registry = new MessageHandlerRegistry();
        registry.Register(typeof(InboxTestEvent), ConsumerType, typeof(RecordingInboxHandler));

        return new InboxProcessor(
            store,
            registry,
            serializer ?? RoundTripSerializer(),
            new TestExecutionScopeFactory(provider.GetRequiredService<IServiceScopeFactory>()),
            options ?? DefaultOptions,
            new FakeTimeProvider(Now),
            random ?? FixedRandom.NoJitter,
            NullLogger<InboxProcessor>.Instance);
    }

    /// <summary>
    /// Same processor, with the scope factory handed back so a test can read the
    /// <see cref="IExecutionContext"/> the processor built for each message.
    /// </summary>
    private static InboxProcessor BuildRecordingScopes(
        IInboxProcessorStore store,
        out TestExecutionScopeFactory scopes)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new RecordingInboxHandler());
        services.AddSingleton<IInboxSettlementStore>(new ScriptedInboxSettlementStore());
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        scopes = new TestExecutionScopeFactory(provider.GetRequiredService<IServiceScopeFactory>());

        var registry = new MessageHandlerRegistry();
        registry.Register(typeof(InboxTestEvent), ConsumerType, typeof(RecordingInboxHandler));

        return new InboxProcessor(
            store,
            registry,
            RoundTripSerializer(),
            scopes,
            DefaultOptions,
            new FakeTimeProvider(Now),
            FixedRandom.NoJitter,
            NullLogger<InboxProcessor>.Instance);
    }

    private static IMessageSerializer RoundTripSerializer()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        serializer.Deserialize(Arg.Any<string>(), Arg.Any<string>())
            .Returns(_ => new InboxTestEvent(MessageId.New(), "tenant-a"));

        return serializer;
    }
}
