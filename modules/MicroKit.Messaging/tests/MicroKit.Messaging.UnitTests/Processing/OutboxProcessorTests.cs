using Microsoft.Extensions.Time.Testing;

namespace MicroKit.Messaging.UnitTests.Processing;

/// <summary>
/// Batch-engine behaviour, with no database. The clock and the jitter draw are both injected,
/// so every assertion below is an exact value rather than a range or a tolerance.
/// </summary>
public sealed class OutboxProcessorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    private static OutboxProcessorOptions DefaultOptions => new()
    {
        BatchSize = 10,
        PollingInterval = TimeSpan.FromMilliseconds(10),
        MaxRetries = 3,
        LockDuration = TimeSpan.FromMinutes(1),
        MaxRetryBackoff = TimeSpan.FromHours(1),
    };

    // ---------------------------------------------------------------------------
    // Back-off computation (deterministic half) — exact values, jitter neutralised
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 1)]      // 2^0
    [InlineData(1, 2)]      // 2^1
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(5, 32)]
    [InlineData(10, 1024)]
    public void ComputeBackoffCeiling_BelowCap_IsTwoToThePowerOfRetryCountSeconds(
        int retryCount, int expectedSeconds)
    {
        var sut = Build(Substitute.For<IOutboxProcessorStore>(), new ScriptedDispatcher());

        sut.ComputeBackoffCeiling(retryCount).ShouldBe(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public void ComputeBackoffCeiling_AboveCap_IsClampedToMaxRetryBackoff()
    {
        var options = DefaultOptions with { MaxRetryBackoff = TimeSpan.FromSeconds(3600) };
        var sut = Build(Substitute.For<IOutboxProcessorStore>(), new ScriptedDispatcher(), options);

        // 2^11 = 2048 s is still under the cap; 2^12 = 4096 s is over it.
        sut.ComputeBackoffCeiling(11).ShouldBe(TimeSpan.FromSeconds(2048));
        sut.ComputeBackoffCeiling(12).ShouldBe(TimeSpan.FromSeconds(3600));
        sut.ComputeBackoffCeiling(50).ShouldBe(TimeSpan.FromSeconds(3600));
    }

    [Fact]
    public void ComputeBackoffCeiling_WithCorruptRetryCount_DoesNotOverflow()
    {
        var sut = Build(Substitute.For<IOutboxProcessorStore>(), new ScriptedDispatcher());

        // The exponent is clamped before shifting, so int.MaxValue cannot shift past 1L << 20.
        Should.NotThrow(() => sut.ComputeBackoffCeiling(int.MaxValue));
        sut.ComputeBackoffCeiling(-5).ShouldBe(TimeSpan.FromSeconds(1));
    }

    // ---------------------------------------------------------------------------
    // Jitter bounds — the one place a range IS the correct assertion
    // ---------------------------------------------------------------------------

    [Fact]
    public void ApplyJitter_AlwaysFallsWithinZeroAndTheCeiling()
    {
        var sut = Build(Substitute.For<IOutboxProcessorStore>(), new ScriptedDispatcher(), random: Random.Shared);
        var ceiling = TimeSpan.FromSeconds(64);

        for (var i = 0; i < 1_000; i++)
        {
            var jittered = sut.ApplyJitter(ceiling);

            jittered.ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
            jittered.ShouldBeLessThanOrEqualTo(ceiling);
        }
    }

    [Fact]
    public void ApplyJitter_SpreadsDrawsAcrossTheInterval()
    {
        // The whole point of jitter: two messages failing together must not retry together.
        var sut = Build(Substitute.For<IOutboxProcessorStore>(), new ScriptedDispatcher(), random: Random.Shared);
        var ceiling = TimeSpan.FromHours(1);

        var draws = Enumerable.Range(0, 50).Select(_ => sut.ApplyJitter(ceiling)).ToList();

        draws.Distinct().Count().ShouldBeGreaterThan(1, "full jitter must not be deterministic");
    }

    [Fact]
    public void ApplyJitter_WithNeutralisedDraw_ReturnsTheCeilingExactly()
    {
        var sut = Build(Substitute.For<IOutboxProcessorStore>(), new ScriptedDispatcher());

        sut.ApplyJitter(TimeSpan.FromSeconds(64)).ShouldBe(TimeSpan.FromSeconds(64));
    }

    // ---------------------------------------------------------------------------
    // Retry / dead-letter threshold
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ProcessBatch_WhenDispatchThrows_BelowThreshold_SchedulesExactRetryInstant()
    {
        var message = OutboxFixtures.Message(retryCount: 1);
        var (store, captured) = StoreReturning(OutboxFixtures.Claim(message));
        var dispatcher = new ScriptedDispatcher(_ => new InvalidOperationException("transient"));

        var result = await Build(store, dispatcher).ProcessBatchAsync(10, CancellationToken.None);

        var outcome = captured.Single();
        outcome.Kind.ShouldBe(OutboxOutcomeKind.Retry);
        outcome.RetryCount.ShouldBe(2, "the processor persists RetryCount + 1");
        // 2^2 = 4 s, jitter neutralised by FixedRandom.NoJitter. Exact, not approximate.
        outcome.NextRetryAtUtc.ShouldBe(Now.AddSeconds(4));
        outcome.ErrorMessage.ShouldBe("transient");
        result.Retried.ShouldBe(1);
        result.DeadLettered.ShouldBe(0);
    }

    [Fact]
    public async Task ProcessBatch_WhenDispatchThrows_AtThreshold_DeadLettersInsteadOfRetrying()
    {
        // MaxRetries = 3, so a message already at 2 reaches 3 on this failure.
        var message = OutboxFixtures.Message(retryCount: 2);
        var (store, captured) = StoreReturning(OutboxFixtures.Claim(message));
        var dispatcher = new ScriptedDispatcher(_ => new InvalidOperationException("still failing"));

        var result = await Build(store, dispatcher).ProcessBatchAsync(10, CancellationToken.None);

        var outcome = captured.Single();
        outcome.Kind.ShouldBe(OutboxOutcomeKind.DeadLetter);
        outcome.RetryCount.ShouldBe(3);
        outcome.NextRetryAtUtc.ShouldBeNull();
        result.DeadLettered.ShouldBe(1);
        result.Retried.ShouldBe(0);
    }

    // ---------------------------------------------------------------------------
    // OutboxPayloadException — dead-letters on FIRST sight
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ProcessBatch_WhenPayloadPermanentlyUndeliverable_DeadLettersWithoutConsumingRetries()
    {
        var message = OutboxFixtures.Message(retryCount: 0);
        var (store, captured) = StoreReturning(OutboxFixtures.Claim(message));
        var dispatcher = new ScriptedDispatcher(_ => new OutboxPayloadException("unresolvable type"));

        var result = await Build(store, dispatcher).ProcessBatchAsync(10, CancellationToken.None);

        var outcome = captured.Single();
        outcome.Kind.ShouldBe(OutboxOutcomeKind.DeadLetter);
        outcome.RetryCount.ShouldBe(
            0, "a poison message is dead-lettered on sight — the retry budget is never spent");
        result.DeadLettered.ShouldBe(1);
        dispatcher.Dispatched.Count.ShouldBe(1, "exactly one dispatch, not MaxRetries of them");
    }

    [Fact]
    public async Task ProcessBatch_WhenOneMessageIsPoison_TheRestOfTheBatchStillRuns()
    {
        var poison = OutboxFixtures.Message();
        var healthy = OutboxFixtures.Message();
        var (store, captured) = StoreReturning(OutboxFixtures.Claim(poison, healthy));
        var dispatcher = new ScriptedDispatcher(
            m => m.Id == poison.Id ? new OutboxPayloadException("bad payload") : null);

        var result = await Build(store, dispatcher).ProcessBatchAsync(10, CancellationToken.None);

        result.DeadLettered.ShouldBe(1);
        result.Published.ShouldBe(1);
        result.WasAborted.ShouldBeFalse("a poison message kills itself, not the batch");
        captured.Count.ShouldBe(2);
    }

    // ---------------------------------------------------------------------------
    // OutboxTransportUnavailableException — releases the remainder, consumes no retries
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ProcessBatch_WhenTransportUnavailable_ReleasesRemainderWithoutConsumingRetries()
    {
        var first = OutboxFixtures.Message();
        var second = OutboxFixtures.Message();
        var third = OutboxFixtures.Message();
        var (store, captured) = StoreReturning(OutboxFixtures.Claim(first, second, third));
        var dispatcher = new ScriptedDispatcher(
            m => m.Id == second.Id ? new OutboxTransportUnavailableException("broker down") : null);

        var result = await Build(store, dispatcher).ProcessBatchAsync(10, CancellationToken.None);

        result.AbortReason.ShouldBe(OutboxBatchAbortReason.TransportUnavailable);
        result.Published.ShouldBe(1);
        result.Released.ShouldBe(2, "the message that hit the outage AND every message after it");
        result.Retried.ShouldBe(0, "an outage must not burn the queue's retry budget");
        result.DeadLettered.ShouldBe(0);

        // The message that triggered the outage is released, not retried.
        captured.Single(o => o.MessageId == second.Id).Kind.ShouldBe(OutboxOutcomeKind.Released);
        captured.Single(o => o.MessageId == third.Id).Kind.ShouldBe(OutboxOutcomeKind.Released);
        dispatcher.Dispatched.Count.ShouldBe(2, "the third message is never attempted");
    }

    [Fact]
    public async Task ProcessBatch_WhenTransportUnavailable_StillSettlesEveryClaimedMessage()
    {
        var messages = Enumerable.Range(0, 5).Select(_ => OutboxFixtures.Message()).ToArray();
        var (store, captured) = StoreReturning(OutboxFixtures.Claim(messages));
        var dispatcher = new ScriptedDispatcher(_ => new OutboxTransportUnavailableException("down"));

        await Build(store, dispatcher).ProcessBatchAsync(10, CancellationToken.None);

        captured.Count.ShouldBe(5, "an unsettled outcome is a stranded lease");
    }

    // ---------------------------------------------------------------------------
    // OutboxConfigurationException — settles the batch, THEN rethrows
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ProcessBatch_WhenDispatcherUnregistered_SettlesBatchThenRethrows()
    {
        var messages = Enumerable.Range(0, 3).Select(_ => OutboxFixtures.Message()).ToArray();
        var (store, captured) = StoreReturning(OutboxFixtures.Claim(messages));

        // No IOutboxDispatcher registered at all — the resolution itself is the fault.
        var sut = BuildWithoutDispatcher(store);

        await Should.ThrowAsync<OutboxConfigurationException>(
            async () => await sut.ProcessBatchAsync(10, CancellationToken.None));

        captured.Count.ShouldBe(3, "the batch is settled BEFORE the rethrow, or leases are stranded");
        captured.ShouldAllBe(o => o.Kind == OutboxOutcomeKind.Released);
    }

    [Fact]
    public async Task ProcessBatch_WhenDispatcherUnregistered_ConsumesNoRetryBudget()
    {
        var message = OutboxFixtures.Message(retryCount: 0);
        var (store, captured) = StoreReturning(OutboxFixtures.Claim(message));
        var sut = BuildWithoutDispatcher(store);

        await Should.ThrowAsync<OutboxConfigurationException>(
            async () => await sut.ProcessBatchAsync(10, CancellationToken.None));

        captured.Single().RetryCount.ShouldBe(
            0, "a missing registration must not dead-letter the whole queue");
    }

    // ---------------------------------------------------------------------------
    // Nominal path and claim handling
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ProcessBatch_WhenClaimEmpty_DoesNotDispatchOrSettle()
    {
        var store = Substitute.For<IOutboxProcessorStore>();
        store.ClaimBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(OutboxClaim.Empty));
        var dispatcher = new ScriptedDispatcher();

        var result = await Build(store, dispatcher).ProcessBatchAsync(10, CancellationToken.None);

        result.ShouldBe(OutboxBatchResult.Empty);
        dispatcher.Dispatched.ShouldBeEmpty();
        await store.DidNotReceive().ApplyOutcomesAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyList<OutboxOutcome>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatch_WhenAllSucceed_PublishesEveryMessageAndSettlesOnce()
    {
        var messages = Enumerable.Range(0, 4).Select(_ => OutboxFixtures.Message()).ToArray();
        var claim = OutboxFixtures.Claim(messages);
        var (store, captured) = StoreReturning(claim);
        var dispatcher = new ScriptedDispatcher();

        var result = await Build(store, dispatcher).ProcessBatchAsync(10, CancellationToken.None);

        result.Claimed.ShouldBe(4);
        result.Published.ShouldBe(4);
        result.WasAborted.ShouldBeFalse();
        dispatcher.Dispatched.Select(m => m.Id).ShouldBe(messages.Select(m => m.Id), "claim order is dispatch order");
        captured.ShouldAllBe(o => o.Kind == OutboxOutcomeKind.Published);

        // One settlement for the whole batch — not one write per message.
        await store.Received(1).ApplyOutcomesAsync(
            claim.Token, Arg.Any<IReadOnlyList<OutboxOutcome>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatch_SettlesUnderTheClaimToken()
    {
        var claim = OutboxFixtures.Claim(OutboxFixtures.Message());
        var (store, _) = StoreReturning(claim);

        await Build(store, new ScriptedDispatcher()).ProcessBatchAsync(10, CancellationToken.None);

        // The token is the whole defence against a stale processor overwriting a fresh one.
        await store.Received(1).ApplyOutcomesAsync(
            claim.Token, Arg.Any<IReadOnlyList<OutboxOutcome>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatch_WhenSettlementThrows_DoesNotPropagate()
    {
        // Leases expire on their own; taking the host down over a settlement fault would be worse.
        var store = Substitute.For<IOutboxProcessorStore>();
        store.ClaimBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(OutboxFixtures.Claim(OutboxFixtures.Message())));
        store.ApplyOutcomesAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<OutboxOutcome>>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromException<int>(new InvalidOperationException("db down")));

        var result = await Build(store, new ScriptedDispatcher()).ProcessBatchAsync(10, CancellationToken.None);

        result.Published.ShouldBe(1);
    }

    [Fact]
    public async Task ProcessBatch_WhenErrorMessageExceedsLimit_TruncatesIt()
    {
        var options = DefaultOptions with { MaxErrorMessageLength = 32 };
        var (store, captured) = StoreReturning(OutboxFixtures.Claim(OutboxFixtures.Message()));
        var dispatcher = new ScriptedDispatcher(_ => new InvalidOperationException(new string('x', 500)));

        await Build(store, dispatcher, options).ProcessBatchAsync(10, CancellationToken.None);

        captured.Single().ErrorMessage!.Length.ShouldBe(32);
    }

    [Fact]
    public async Task ProcessBatch_WhenCancelledMidBatch_ReleasesRemainderWithoutConsumingRetries()
    {
        using var cts = new CancellationTokenSource();
        var first = OutboxFixtures.Message();
        var second = OutboxFixtures.Message();
        var (store, captured) = StoreReturning(OutboxFixtures.Claim(first, second));
        var dispatcher = new ScriptedDispatcher(m =>
        {
            if (m.Id == first.Id)
            {
                cts.Cancel();
            }

            return null;
        });

        var result = await Build(store, dispatcher).ProcessBatchAsync(10, cts.Token);

        result.AbortReason.ShouldBe(OutboxBatchAbortReason.Cancelled);
        result.Published.ShouldBe(1);
        result.Released.ShouldBe(1);
        captured.Single(o => o.MessageId == second.Id).Kind.ShouldBe(OutboxOutcomeKind.Released);
    }

    // ---------------------------------------------------------------------------
    // helpers
    // ---------------------------------------------------------------------------

    private static (IOutboxProcessorStore Store, List<OutboxOutcome> Captured) StoreReturning(OutboxClaim claim)
    {
        var captured = new List<OutboxOutcome>();
        var store = Substitute.For<IOutboxProcessorStore>();

        store.ClaimBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(claim));

        store.ApplyOutcomesAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<OutboxOutcome>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                captured.AddRange(ci.Arg<IReadOnlyList<OutboxOutcome>>());
                return ValueTask.FromResult(captured.Count);
            });

        return (store, captured);
    }

    private static OutboxProcessor Build(
        IOutboxProcessorStore store,
        ScriptedDispatcher dispatcher,
        OutboxProcessorOptions? options = null,
        Random? random = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOutboxDispatcher>(dispatcher);
        services.AddLogging();

        return BuildCore(store, services, options, random);
    }

    private static OutboxProcessor BuildWithoutDispatcher(IOutboxProcessorStore store)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        return BuildCore(store, services, options: null, random: null);
    }

    private static OutboxProcessor BuildCore(
        IOutboxProcessorStore store,
        ServiceCollection services,
        OutboxProcessorOptions? options,
        Random? random)
    {
        var provider = services.BuildServiceProvider();

        return new OutboxProcessor(
            store,
            new TestExecutionScopeFactory(provider.GetRequiredService<IServiceScopeFactory>()),
            options ?? DefaultOptions,
            new FakeTimeProvider(Now),
            random ?? FixedRandom.NoJitter,
            NullLogger<OutboxProcessor>.Instance);
    }
}
