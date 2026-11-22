using Microsoft.Extensions.Time.Testing;
using MicroKit.Messaging.Stores;

namespace MicroKit.Messaging.UnitTests.Processing;

/// <summary>
/// Batch-engine behaviour, with no database. The clock and the jitter draw are both injected,
/// so every assertion below is an exact value rather than a range or a tolerance.
/// </summary>
public sealed class OutboxProcessorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly MessageStoreKey TestStore = new("tests:outbox-store");

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
    public async Task ProcessBatch_WhenTransportUnavailable_DefersItsEndpointsRemainderWithoutConsumingRetries()
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
        result.Released.ShouldBe(2, "the message that hit the outage AND every later row of its endpoint");
        result.Retried.ShouldBe(0, "an outage must not burn the queue's retry budget");
        result.DeadLettered.ShouldBe(0);

        // The message that triggered the outage is deferred, its attempt count unchanged.
        captured.Where(o => o.MessageId != first.Id).ShouldAllBe(o =>
            o.Kind == OutboxOutcomeKind.Deferred && o.NextRetryAtUtc != null);
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

        // No dispatcher registered for the row's endpoint — the resolution itself is the fault.
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
    // Dispatcher activation failure — batch-fatal, no retry budget, no rethrow
    // ---------------------------------------------------------------------------

    /// <summary>
    /// A dispatcher that cannot be activated for one tenant's row defers that tenant's rows only (#124
    /// D1): no retry budget moves, nothing is rethrown, the endpoint stays open, and the other tenants'
    /// rows behind it are dispatched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The fixture fails activation for message 2 only, and that shape is realistic.</b> Activation
    /// runs in the message's own scope, built from its <c>TenantId</c>, so a tenant-scoped cause fails
    /// for some messages and not others. Message 3 belongs to another tenant: it is the probe, and its
    /// success keeps the endpoint open.
    /// </para>
    /// <para>
    /// The fault is an <see cref="InvalidOperationException"/> on purpose — the type the container
    /// throws for a dependency it cannot supply, and the only one the resolution tags. Every other type
    /// stays per message, pinned by
    /// <c>ProcessBatch_WhenActivationThrowsAnythingButInvalidOperationException_RetriesOnlyThatMessage</c>.
    /// The row with no tenant keeps #188's endpoint verdict:
    /// <c>ProcessBatch_WhenTheDispatcherCannotBeActivatedForARowWithNoTenant_DefersItsEndpointsRows</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ProcessBatch_WhenTheDispatcherCannotBeActivatedForOneTenant_DefersThatTenantsRows_AndDispatchesTheOthers()
    {
        var first = OutboxFixtures.Message(retryCount: 1);
        // Its own tenant and kind, so the 1009 assertion below cannot pass on a neighbour's values.
        var second = OutboxFixtures.ContractMessage(retryCount: 1, tenantId: "tenant-b");
        var third = OutboxFixtures.Message(retryCount: 1);
        var claim = OutboxFixtures.Claim(first, second, third);
        var (store, captured) = StoreReturning(claim);

        var dispatcher = new ScriptedDispatcher();
        var logger = new CapturingLogger<OutboxProcessor>();
        var backoff = new OutboxEndpointBackoff(DefaultOptions.PollingInterval, DefaultOptions.TransportUnavailableBackoff);
        var sut = BuildFailingActivationFor(
            store,
            dispatcher,
            second.Id,
            () => new InvalidOperationException(
                "Unable to resolve service for type 'IMessageTransport' while attempting to " +
                "activate 'TransportOutboxDispatcher'."),
            logger,
            backoff);

        // Not rethrown: a registered dispatcher that cannot be built must not stop the worker.
        var result = await sut.ProcessBatchAsync(10, CancellationToken.None);

        result.AbortReason.ShouldBe(OutboxBatchAbortReason.DispatcherActivationFailed);
        logger.Records.ShouldContain(
            r => r.EventId == 1009 && r.Level == LogLevel.Error,
            "a deferral with no rethrow and no retry movement must not be silent");

        // The row an operator has to find when the cause never clears.
        var activationFailure = logger.Records.Single(r => r.EventId == 1009).Message;
        activationFailure.ShouldContain(second.Id.Value.ToString());
        activationFailure.ShouldContain("tenant-b");
        activationFailure.ShouldContain("endpoint", Case.Sensitive, "the row's endpoint names the dispatcher that failed");
        logger.Records.ShouldContain(r => r.EventId == 1011 && r.Message.Contains("tenant-b"), "1011 says what waits for that tenant");
        logger.Records.ShouldNotContain(r => r.EventId == 1010, "the endpoint is not down");

        result.Published.ShouldBe(2, "the other tenant's rows, before and after, are dispatched");
        result.Released.ShouldBe(1, "only the row of the tenant that failed waits");
        result.Retried.ShouldBe(0, "an activation failure is not a per-message fault");
        result.DeadLettered.ShouldBe(0);

        var deferred = captured.Single(o => o.MessageId == second.Id);
        deferred.Kind.ShouldBe(OutboxOutcomeKind.Deferred, "the row keeps its own count: no attempt is charged");
        deferred.NextRetryAtUtc.ShouldBe(Now + DefaultOptions.PollingInterval, "to the end of the tenant's back-off");
        dispatcher.Dispatched.Select(m => m.Id).ShouldBe([first.Id, third.Id]);
        backoff.BlockedAt(Now).ShouldBe(OutboxEndpoints.None, "the endpoint stays open for every other tenant");
        backoff.TenantsOf(OutboxDispatchers.Transport).IsBlocked("tenant-b", Now, out _).ShouldBeTrue();
        await store.Received(1).ApplyOutcomesAsync(
            claim.Token, Arg.Any<IReadOnlyList<OutboxOutcome>>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A tenant in back-off on an endpoint is left out of the claim on that endpoint only (#124 A7), and
    /// a row of it handed over anyway — a store that does not honour the tenant filter — is deferred and
    /// reported to the loop, as an endpoint's is. With no tenant in back-off both lists are empty.
    /// </summary>
    [Fact]
    public async Task ProcessBatch_LeavesATenantInBackOffOutOfTheClaim_OnItsEndpointOnly_AndReportsARowOfItHandedOverAnyway()
    {
        var blocked = OutboxFixtures.Message(tenantId: "tenant-b");
        var other = OutboxFixtures.Message(tenantId: "tenant-a");
        var claim = OutboxFixtures.Claim(blocked, other);
        var (store, captured) = StoreReturning(claim);
        var lists = new List<(IReadOnlyList<string> Transport, IReadOnlyList<string> Local)>();
        store.ClaimBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<OutboxEndpoints>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                lists.Add((ci.ArgAt<IReadOnlyList<string>>(3), ci.ArgAt<IReadOnlyList<string>>(4)));
                return ValueTask.FromResult(claim);
            });
        var backoff = new OutboxEndpointBackoff(DefaultOptions.PollingInterval, DefaultOptions.TransportUnavailableBackoff);
        var dispatcher = new ScriptedDispatcher();
        var sut = BuildFailingActivationFor(
            store, dispatcher, MessageId.New(), () => new InvalidOperationException("never"), new CapturingLogger<OutboxProcessor>(), backoff);

        (await sut.ProcessBatchAsync(10, CancellationToken.None)).HandedOverInBackOff.ShouldBeFalse("control: nobody in back-off");
        lists[0].Transport.ShouldBeEmpty();
        lists[0].Local.ShouldBeEmpty();

        backoff.TenantsOf(OutboxDispatchers.Transport).OnDown("tenant-b", Now, out var until);
        captured.Clear();
        dispatcher.Dispatched.Clear();
        var result = await sut.ProcessBatchAsync(10, CancellationToken.None);

        lists[1].Transport.ShouldBe(["tenant-b"], "left out of the transport's rows");
        lists[1].Local.ShouldBeEmpty("its local rows are still claimed");
        result.HandedOverInBackOff.ShouldBeTrue("a row the claim was asked to leave out came back: the loop waits");
        captured.Single(o => o.MessageId == blocked.Id).ShouldBe(OutboxOutcome.Deferred(blocked.Id, until));
        dispatcher.Dispatched.Select(m => m.Id).ShouldBe([other.Id]);
    }

    /// <summary>
    /// A row with no tenant has no tenant to narrow the verdict to: its endpoint is deferred as #188
    /// made it — the row and every later row of the endpoint, no retry consumed, nothing rethrown (#124 V4).
    /// </summary>
    [Fact]
    public async Task ProcessBatch_WhenTheDispatcherCannotBeActivatedForARowWithNoTenant_DefersItsEndpointsRows()
    {
        var first = OutboxFixtures.Message(retryCount: 1, tenantId: null);
        var second = OutboxFixtures.ContractMessage(retryCount: 1, tenantId: null);
        var third = OutboxFixtures.Message(retryCount: 1, tenantId: null);
        var claim = OutboxFixtures.Claim(first, second, third);
        var (store, captured) = StoreReturning(claim);

        var dispatcher = new ScriptedDispatcher();
        var logger = new CapturingLogger<OutboxProcessor>();
        var backoff = new OutboxEndpointBackoff(DefaultOptions.PollingInterval, DefaultOptions.TransportUnavailableBackoff);
        var sut = BuildFailingActivationFor(
            store, dispatcher, second.Id, () => new InvalidOperationException("no IMessageTransport"), logger, backoff);

        var result = await sut.ProcessBatchAsync(10, CancellationToken.None);

        result.AbortReason.ShouldBe(OutboxBatchAbortReason.DispatcherActivationFailed);
        logger.Records.Single(r => r.EventId == 1009).Message.ShouldContain(second.Id.Value.ToString());
        logger.Records.ShouldContain(r => r.EventId == 1010, "the endpoint is down");
        logger.Records.ShouldNotContain(r => r.EventId == 1011, "no tenant is involved");

        result.Published.ShouldBe(1, "message 1 was delivered before message 2's dispatcher failed");
        result.Released.ShouldBe(2, "the message that hit the fault AND every later row of its endpoint");
        result.Retried.ShouldBe(0);
        foreach (var deferred in captured.Where(o => o.MessageId != first.Id))
        {
            deferred.Kind.ShouldBe(OutboxOutcomeKind.Deferred);
            deferred.NextRetryAtUtc.ShouldBe(Now + DefaultOptions.PollingInterval);
        }

        dispatcher.Dispatched.ShouldHaveSingleItem().Id.ShouldBe(first.Id, "message 3 is never attempted");
        backoff.BlockedAt(Now).ShouldBe(OutboxEndpoints.Transport);
        backoff.TenantsOf(OutboxDispatchers.Transport).IsEmpty.ShouldBeTrue();
    }

    /// <summary>
    /// An <see cref="InvalidOperationException"/> thrown by a dispatcher that activated fine is still
    /// one message's transient fault — the activation arm must not be a bare catch on that type.
    /// </summary>
    [Fact]
    public async Task ProcessBatch_WhenTheDispatcherItselfThrowsInvalidOperationException_RetriesOnlyThatMessage()
    {
        var first = OutboxFixtures.Message();
        var second = OutboxFixtures.Message();
        var third = OutboxFixtures.Message();
        var (store, captured) = StoreReturning(OutboxFixtures.Claim(first, second, third));
        var dispatcher = new ScriptedDispatcher(
            m => m.Id == second.Id ? new InvalidOperationException("channel closed mid-send") : null);

        var result = await Build(store, dispatcher).ProcessBatchAsync(10, CancellationToken.None);

        result.AbortReason.ShouldBe(OutboxBatchAbortReason.None);
        result.Published.ShouldBe(2);
        result.Retried.ShouldBe(1);
        result.Released.ShouldBe(0);

        var retried = captured.Single(o => o.MessageId == second.Id);
        retried.Kind.ShouldBe(OutboxOutcomeKind.Retry);
        retried.RetryCount.ShouldBe(1);
        // 2^1 = 2 s, jitter neutralised by FixedRandom.NoJitter.
        retried.NextRetryAtUtc.ShouldBe(Now.AddSeconds(2));

        dispatcher.Dispatched.Count.ShouldBe(3, "the batch continues past the failing message");
    }

    /// <summary>
    /// Only the container's <see cref="InvalidOperationException"/> is an activation failure. Any other
    /// type thrown while the dispatcher is being built is classified as if the dispatcher had thrown
    /// it: that message is retried, the rest of the batch runs, and event 1009 is not written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A tenant lookup failing with its own type — <see cref="KeyNotFoundException"/> here — is the
    /// case a catch wider than <see cref="InvalidOperationException"/> moves from a per-message retry,
    /// which dead-letters while every other tenant keeps publishing, into the batch-wide verdict, which
    /// stalls the outbox for every tenant (ADR-MSG-019, known defect). This test keeps it on the path it
    /// had before the resolution was reworked.
    /// </para>
    /// <para>
    /// The fixture is test 1's, message for message, so the two tests differ in the exception type
    /// alone. Message 3 must still be dispatched: that is what separates a per-message verdict from a
    /// batch-wide one.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ProcessBatch_WhenActivationThrowsAnythingButInvalidOperationException_RetriesOnlyThatMessage()
    {
        var first = OutboxFixtures.Message(retryCount: 1);
        var second = OutboxFixtures.ContractMessage(retryCount: 1, tenantId: "tenant-b");
        var third = OutboxFixtures.Message(retryCount: 1);
        var (store, captured) = StoreReturning(OutboxFixtures.Claim(first, second, third));

        var dispatcher = new ScriptedDispatcher();
        var logger = new CapturingLogger<OutboxProcessor>();
        var sut = BuildFailingActivationFor(
            store,
            dispatcher,
            second.Id,
            () => new KeyNotFoundException("No connection string is configured for tenant 'tenant-b'."),
            logger);

        var result = await sut.ProcessBatchAsync(10, CancellationToken.None);

        result.AbortReason.ShouldBe(
            OutboxBatchAbortReason.None, "one message's fault must not abandon the batch");
        logger.Records.ShouldNotContain(
            r => r.EventId == 1009, "only InvalidOperationException is an activation failure");

        result.Published.ShouldBe(2);
        result.Retried.ShouldBe(1);
        result.Released.ShouldBe(0);

        var retried = captured.Single(o => o.MessageId == second.Id);
        retried.Kind.ShouldBe(OutboxOutcomeKind.Retry);
        retried.RetryCount.ShouldBe(2, "the processor persists RetryCount + 1");
        // 2^2 = 4 s, jitter neutralised by FixedRandom.NoJitter.
        retried.NextRetryAtUtc.ShouldBe(Now.AddSeconds(4));
        retried.ErrorMessage.ShouldBe("No connection string is configured for tenant 'tenant-b'.");

        dispatcher.Dispatched.Select(m => m.Id).ShouldBe(
            new[] { first.Id, third.Id }, "the batch continues past the failing message");
    }

    /// <summary>
    /// A cancellation raised while the dispatcher is being activated is the host shutting down, not
    /// an activation fault: the batch is cancelled, and event 1009 is not written.
    /// </summary>
    /// <remarks>
    /// <see cref="OperationCanceledException"/> does not derive from
    /// <see cref="InvalidOperationException"/>, the only type <c>ResolveDispatcher</c> tags, so nothing
    /// in the tagging filter names it and this test no longer pins that filter: it pins the type
    /// hierarchy. It still guards against a catch widened past <see cref="InvalidOperationException"/>
    /// that forgets to leave cancellation out.
    /// </remarks>
    [Fact]
    public async Task ProcessBatch_WhenDispatcherActivationIsCancelled_ReleasesAsCancelledRatherThanAsAnActivationFault()
    {
        using var cts = new CancellationTokenSource();
        var first = OutboxFixtures.Message();
        var second = OutboxFixtures.Message();
        var third = OutboxFixtures.Message();
        var (store, _) = StoreReturning(OutboxFixtures.Claim(first, second, third));

        var logger = new CapturingLogger<OutboxProcessor>();
        var sut = BuildFailingActivationFor(
            store,
            new ScriptedDispatcher(),
            second.Id,
            () =>
            {
                cts.Cancel();
                return new OperationCanceledException(cts.Token);
            },
            logger);

        var result = await sut.ProcessBatchAsync(10, cts.Token);

        result.AbortReason.ShouldBe(OutboxBatchAbortReason.Cancelled);
        result.Published.ShouldBe(1);
        result.Released.ShouldBe(2);
        result.Retried.ShouldBe(0);
        logger.Records.ShouldNotContain(r => r.EventId == 1009, "shutdown is not an activation fault");
    }

    /// <summary>
    /// A provider disposed at shutdown is neither an activation fault nor a failure: the row it hit
    /// is released with its attempt count unchanged, like the rest of the batch (#128, #188 D10).
    /// </summary>
    /// <remarks>
    /// <see cref="ObjectDisposedException"/> derives from <see cref="InvalidOperationException"/>,
    /// not from <see cref="OperationCanceledException"/>. Before #188 it reached the transient arm
    /// and charged the row a retry; the shutdown arm now classifies it as shutdown.
    /// </remarks>
    [Fact]
    public async Task ProcessBatch_WhenTheProviderIsDisposedDuringActivation_ReleasesTheRowWithoutChargingARetry()
    {
        using var cts = new CancellationTokenSource();
        var first = OutboxFixtures.Message();
        var second = OutboxFixtures.Message();
        var third = OutboxFixtures.Message();
        var (store, captured) = StoreReturning(OutboxFixtures.Claim(first, second, third));

        var logger = new CapturingLogger<OutboxProcessor>();
        var sut = BuildFailingActivationFor(
            store,
            new ScriptedDispatcher(),
            second.Id,
            () =>
            {
                // Shutdown as it actually arrives: the token is cancelled, then the provider goes.
                cts.Cancel();
                return new ObjectDisposedException(nameof(IServiceProvider));
            },
            logger);

        var result = await sut.ProcessBatchAsync(10, cts.Token);

        result.AbortReason.ShouldBe(OutboxBatchAbortReason.Cancelled);
        logger.Records.ShouldNotContain(r => r.EventId == 1009, "shutdown is not an activation fault");

        captured.Single(o => o.MessageId == first.Id).Kind.ShouldBe(OutboxOutcomeKind.Published);
        captured.Single(o => o.MessageId == second.Id).Kind.ShouldBe(
            OutboxOutcomeKind.Released, "a host stop is never a message failure");
        captured.Single(o => o.MessageId == third.Id).Kind.ShouldBe(OutboxOutcomeKind.Released);
        captured.ShouldNotContain(o => o.Kind == OutboxOutcomeKind.Retry);
    }

    /// <summary>
    /// The same exception while the host keeps running is a failure of that row, not a stop
    /// (#188 review): a stop releases with no attempt charged, so a recurring fault would be
    /// re-claimed forever without ever reaching the dead-letter.
    /// </summary>
    [Fact]
    public async Task ProcessBatch_WhenTheProviderIsDisposedWhileTheHostRuns_RetriesOnlyThatRow()
    {
        var first = OutboxFixtures.Message();
        var second = OutboxFixtures.Message();
        var third = OutboxFixtures.Message();
        var (store, captured) = StoreReturning(OutboxFixtures.Claim(first, second, third));

        var sut = BuildFailingActivationFor(
            store,
            new ScriptedDispatcher(),
            second.Id,
            () => new ObjectDisposedException(nameof(IServiceProvider)),
            new CapturingLogger<OutboxProcessor>());

        var result = await sut.ProcessBatchAsync(10, CancellationToken.None);

        result.AbortReason.ShouldBe(OutboxBatchAbortReason.None, "the host is running: nothing was stopped");
        captured.Single(o => o.MessageId == first.Id).Kind.ShouldBe(OutboxOutcomeKind.Published);
        captured.Single(o => o.MessageId == second.Id).Kind.ShouldBe(OutboxOutcomeKind.Retry);
        captured.Single(o => o.MessageId == third.Id).Kind.ShouldBe(OutboxOutcomeKind.Published);
    }

    // ---------------------------------------------------------------------------
    // Nominal path and claim handling
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ProcessBatch_WhenClaimEmpty_DoesNotDispatchOrSettle()
    {
        var store = Substitute.For<IOutboxProcessorStore>();
        store.ClaimBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<OutboxEndpoints>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
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
        store.ClaimBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<OutboxEndpoints>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
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

        store.ClaimBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<OutboxEndpoints>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(claim));

        store.ApplyOutcomesAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<OutboxOutcome>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                captured.AddRange(ci.Arg<IReadOnlyList<OutboxOutcome>>());
                return ValueTask.FromResult(captured.Count);
            });

        return (store, captured);
    }

    // ---------------------------------------------------------------------------
    // The delivery store — what makes a publication in a dispatch scope land in this store
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Every dispatch scope names the store the row came from, so a notification handler's
    /// publication is enlisted in that store (#188, D3 rule 2).
    /// </summary>
    /// <remarks>
    /// Asserted through <b>constructor injection</b>, the path the bus uses: Microsoft DI activates
    /// constructor dependencies from its own scope, so a value written anywhere else would reach a
    /// direct <c>GetService</c> call and nothing else.
    /// </remarks>
    [Fact]
    public async Task ProcessBatch_NamesItsStoreOnEveryDispatchScope()
    {
        var (store, _) = StoreReturning(OutboxFixtures.Claim(OutboxFixtures.Message(), OutboxFixtures.Message()));
        var observed = new List<MessageStoreKey?>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(observed);
        services.AddKeyedScoped<IOutboxDispatcher, StoreRecordingDispatcher>(OutboxDispatchers.Transport);

        var sut = BuildCore(store, services, options: null, random: null);

        await sut.ProcessBatchAsync(10, CancellationToken.None);

        observed.ShouldBe([TestStore, TestStore]);
    }

    /// <summary>Reads the holder the way the bus does — as a constructor dependency.</summary>
    private sealed class StoreRecordingDispatcher(
        DeliveryStoreHolder holder, List<MessageStoreKey?> observed) : IOutboxDispatcher
    {
        public ValueTask DispatchAsync(OutboxMessage message, CancellationToken ct = default)
        {
            observed.Add(holder.Store);
            return ValueTask.CompletedTask;
        }
    }

    // ---------------------------------------------------------------------------
    // The causation link — what makes the dispatched row the cause of its scope's work
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Everything staged inside a dispatch scope names the dispatched row as its cause, so
    /// <c>CausationId</c> is derived from <c>message.Id</c> — never copied from the row's own
    /// <c>CausationId</c>.
    /// </summary>
    /// <remarks>
    /// The distinction is the whole finding: copying made every descendant inherit one ancestor's
    /// causation, and since nothing assigns a causation at the root, that value was null on every
    /// row of every path. Seeding the row with a DIFFERENT causation is what separates the two
    /// implementations — under the copy-through version this test reads back
    /// <c>ancestorCause</c> instead of the row's id.
    /// </remarks>
    [Fact]
    public async Task ProcessBatch_NamesTheDispatchedRowAsTheCauseOfWorkInItsScope()
    {
        var ancestorCause = CausationId.New();
        var message = OutboxFixtures.Message();
        message.CausationId = ancestorCause;

        var (store, _) = StoreReturning(OutboxFixtures.Claim(message));
        var sut = BuildRecordingScopes(store, out var scopes);

        await sut.ProcessBatchAsync(10, CancellationToken.None);

        var ctx = scopes.Contexts.ShouldHaveSingleItem();

        ctx.CausationId.ShouldBe(
            message.Id.Value.ToString(),
            "the cause of work in this scope is the row being dispatched");

        ctx.CausationId.ShouldNotBe(
            ancestorCause.Value.ToString(),
            "copying the row's own causation names the grandparent, one hop too far up");
    }

    /// <summary>
    /// A root row — no causation of its own — still causes its scope's work. This is the case the
    /// copy-through implementation got wrong in production: null in, null out, forever.
    /// </summary>
    [Fact]
    public async Task ProcessBatch_WhenTheDispatchedRowIsARoot_StillNamesItAsTheCause()
    {
        var message = OutboxFixtures.Message();
        message.CausationId = null;

        var (store, _) = StoreReturning(OutboxFixtures.Claim(message));
        var sut = BuildRecordingScopes(store, out var scopes);

        await sut.ProcessBatchAsync(10, CancellationToken.None);

        scopes.Contexts.ShouldHaveSingleItem()
            .CausationId.ShouldBe(message.Id.Value.ToString());
    }

    /// <summary>The correlation is copied through unchanged — it identifies the chain, not a hop.</summary>
    [Fact]
    public async Task ProcessBatch_PropagatesCorrelationUnchangedWhileDerivingCausation()
    {
        var message = OutboxFixtures.Message();
        var (store, _) = StoreReturning(OutboxFixtures.Claim(message));
        var sut = BuildRecordingScopes(store, out var scopes);

        await sut.ProcessBatchAsync(10, CancellationToken.None);

        var ctx = scopes.Contexts.ShouldHaveSingleItem();

        ctx.CorrelationId.ShouldBe(message.CorrelationId.Value.ToString());
        ctx.TenantId.ShouldBe(message.TenantId);
    }

    private static OutboxProcessor Build(
        IOutboxProcessorStore store,
        ScriptedDispatcher dispatcher,
        OutboxProcessorOptions? options = null,
        Random? random = null)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IOutboxDispatcher>(OutboxDispatchers.Transport, dispatcher);
        services.AddLogging();

        return BuildCore(store, services, options, random);
    }

    private static OutboxProcessor BuildWithoutDispatcher(IOutboxProcessorStore store)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        return BuildCore(store, services, options: null, random: null);
    }

    /// <summary>
    /// A processor whose container holds a dispatcher registration that throws while being
    /// activated for one message, and activates normally for every other.
    /// </summary>
    /// <remarks>
    /// REGISTERED but unactivatable — a factory that throws, as in <c>InboxProcessorTests.Build</c>.
    /// Not the same as absent: only an absent descriptor makes <c>GetService</c> return null. The
    /// factory reads the causation of the scope being activated — the row being dispatched — to know
    /// which message it is being built for.
    /// </remarks>
    private static OutboxProcessor BuildFailingActivationFor(
        IOutboxProcessorStore store,
        ScriptedDispatcher dispatcher,
        MessageId failOn,
        Func<Exception> fault,
        ILogger<OutboxProcessor> logger,
        OutboxEndpointBackoff? backoff = null)
    {
        // The scope being activated is the last one created; its context names the row being
        // dispatched as its causation.
        TestExecutionScopeFactory? scopes = null;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<DeliveryStoreHolder>();
        services.AddKeyedScoped<IOutboxDispatcher>(OutboxDispatchers.Transport, (_, _) =>
            scopes!.Contexts[^1].CausationId == failOn.Value.ToString()
                ? throw fault()
                : dispatcher);

        var provider = services.BuildServiceProvider();
        scopes = new TestExecutionScopeFactory(provider.GetRequiredService<IServiceScopeFactory>());

        return new OutboxProcessor(
            store, scopes, DefaultOptions, new FakeTimeProvider(Now), FixedRandom.NoJitter, logger, TestStore, backoff);
    }

    private static OutboxProcessor BuildCore(
        IOutboxProcessorStore store,
        ServiceCollection services,
        OutboxProcessorOptions? options,
        Random? random,
        ILogger<OutboxProcessor>? logger = null)
    {
        // Registered by AddMicroKitMessaging() in production; the processor names its store on it.
        services.AddScoped<DeliveryStoreHolder>();

        var provider = services.BuildServiceProvider();

        return new OutboxProcessor(
            store,
            new TestExecutionScopeFactory(provider.GetRequiredService<IServiceScopeFactory>()),
            options ?? DefaultOptions,
            new FakeTimeProvider(Now),
            random ?? FixedRandom.NoJitter,
            logger ?? NullLogger<OutboxProcessor>.Instance,
            TestStore);
    }

    /// <summary>
    /// Same processor, with the scope factory handed back so a test can read the
    /// <see cref="IExecutionContext"/> the processor built for each message.
    /// </summary>
    private static OutboxProcessor BuildRecordingScopes(
        IOutboxProcessorStore store,
        out TestExecutionScopeFactory scopes)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedSingleton<IOutboxDispatcher>(OutboxDispatchers.Transport, new ScriptedDispatcher());
        var provider = services.BuildServiceProvider();
        scopes = new TestExecutionScopeFactory(provider.GetRequiredService<IServiceScopeFactory>());

        return new OutboxProcessor(
            store,
            scopes,
            DefaultOptions,
            new FakeTimeProvider(Now),
            FixedRandom.NoJitter,
            NullLogger<OutboxProcessor>.Instance);
    }
}
