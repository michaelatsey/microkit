using Microsoft.Extensions.Time.Testing;

namespace MicroKit.Messaging.UnitTests.Processing;

using MicroKit.Messaging.Dispatch;
using MicroKit.Messaging.Outbox;

/// <summary>
/// The batch engine driven through the <b>real</b> <c>TransportOutboxDispatcher</c> over a
/// substituted <see cref="IMessageTransport"/>.
/// </summary>
/// <remarks>
/// <para>
/// <c>OutboxProcessorTests</c> already proves each classification arm, but it does so with
/// <c>ScriptedDispatcher</c> — a double that throws whatever the test names. What it cannot prove
/// is that a <i>transport's</i> exception actually reaches those arms: a dispatcher that caught,
/// wrapped or retyped anything on the way would leave every one of those tests green while
/// converting a released batch into a dead-lettered queue in production. That gap is what this file
/// closes.
/// </para>
/// <para>
/// The no-transport tests here are load-bearing for a second reason: the behaviour they pin lives
/// entirely outside the dispatcher's own code — in <c>OutboxProcessor.ResolveDispatcher</c>'s
/// null-versus-throw separation and the batch loop's activation arm, and in the decision to take
/// <see cref="IMessageTransport"/> through the constructor. Nothing in
/// <c>TransportOutboxDispatcher</c> would look wrong if any of them changed.
/// </para>
/// </remarks>
public sealed class OutboxTransportClassificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    private static OutboxProcessorOptions DefaultOptions => new()
    {
        BatchSize = 10,
        MaxRetries = 3,
        LockDuration = TimeSpan.FromMinutes(1),
        MaxRetryBackoff = TimeSpan.FromHours(1),
    };

    // -----------------------------------------------------------------------------------------
    // A transport failure reaches the arm it is supposed to reach
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// A broker outage releases the batch instead of charging every message a retry.
    /// </summary>
    /// <remarks>
    /// This is the property that keeps an hour-long outage from permanently dead-lettering the
    /// queue: without it, one outage fails all N claimed messages, writes N failure rows, burns N
    /// retry budgets, and repeats on every tick.
    /// </remarks>
    [Fact]
    public async Task ProcessBatch_WhenTransportUnavailable_ReleasesEveryMessageWithoutConsumingRetries()
    {
        var messages = Enumerable.Range(0, 4)
            .Select(_ => OutboxFixtures.ContractMessage(retryCount: 1)).ToArray();
        var claim = OutboxFixtures.Claim(messages);
        var (store, captured) = StoreReturning(claim);

        var result = await Build(
            store,
            new RecordingMessageTransport(_ => new OutboxTransportUnavailableException("broker down")))
            .ProcessBatchAsync(10, CancellationToken.None);

        result.AbortReason.ShouldBe(OutboxBatchAbortReason.TransportUnavailable);
        captured.Count.ShouldBe(messages.Length, "every claimed message must carry an outcome");
        captured.ShouldAllBe(o => o.Kind == OutboxOutcomeKind.Released);
        captured.ShouldAllBe(o => o.RetryCount == 0, "Released consumes no retry budget");
    }

    [Fact]
    public async Task ProcessBatch_WhenTransportUnavailable_StillSettlesUnderTheClaimToken()
    {
        var claim = OutboxFixtures.Claim(OutboxFixtures.ContractMessage());
        var (store, _) = StoreReturning(claim);

        await Build(store, new RecordingMessageTransport(_ => new OutboxTransportUnavailableException()))
            .ProcessBatchAsync(10, CancellationToken.None);

        // A stranded lease blocks its message for the whole LockDuration — worse than the outage.
        await store.Received(1).ApplyOutcomesAsync(
            claim.Token, Arg.Any<IReadOnlyList<OutboxOutcome>>(), Arg.Any<CancellationToken>());
    }

    /// <summary>An unrecognised transport failure stays transient — the conservative default.</summary>
    [Fact]
    public async Task ProcessBatch_WhenTransportFailsUnclassified_RetriesWithBackoff()
    {
        var message = OutboxFixtures.ContractMessage(retryCount: 0);
        var (store, captured) = StoreReturning(OutboxFixtures.Claim(message));

        var result = await Build(
            store, new RecordingMessageTransport(_ => new InvalidOperationException("channel hiccup")))
            .ProcessBatchAsync(10, CancellationToken.None);

        result.Retried.ShouldBe(1);
        var outcome = captured.ShouldHaveSingleItem();
        outcome.Kind.ShouldBe(OutboxOutcomeKind.Retry);
        outcome.RetryCount.ShouldBe(1);

        // FixedRandom.NoJitter draws 1.0, so the delay IS the ceiling: 2^1 = 2 s.
        outcome.NextRetryAtUtc.ShouldBe(Now.AddSeconds(2));
    }

    [Fact]
    public async Task ProcessBatch_WhenTransportRejectsThePayload_DeadLettersOnFirstSight()
    {
        var message = OutboxFixtures.ContractMessage(retryCount: 0);
        var (store, captured) = StoreReturning(OutboxFixtures.Claim(message));

        var result = await Build(
            store, new RecordingMessageTransport(_ => new OutboxPayloadException("over the size limit")))
            .ProcessBatchAsync(10, CancellationToken.None);

        result.DeadLettered.ShouldBe(1);
        var outcome = captured.ShouldHaveSingleItem();
        outcome.Kind.ShouldBe(OutboxOutcomeKind.DeadLetter);
        outcome.RetryCount.ShouldBe(0, "a proven-permanent failure spends no retry budget");
    }

    /// <summary>One poison message does not take the rest of the batch with it.</summary>
    [Fact]
    public async Task ProcessBatch_WhenOneMessageIsRejected_TheRestOfTheBatchStillSends()
    {
        var poison = OutboxFixtures.ContractMessage();
        var healthy = OutboxFixtures.ContractMessage();
        var (store, captured) = StoreReturning(OutboxFixtures.Claim(poison, healthy));

        var transport = new RecordingMessageTransport(
            e => e.Id == poison.Id.Value.ToString() ? new OutboxPayloadException("rejected") : null);

        var result = await Build(store, transport).ProcessBatchAsync(10, CancellationToken.None);

        result.DeadLettered.ShouldBe(1);
        result.Published.ShouldBe(1);
        transport.Sent.ShouldHaveSingleItem().Id.ShouldBe(healthy.Id.Value.ToString());
        captured.Count.ShouldBe(2);
    }

    // -----------------------------------------------------------------------------------------
    // The row's endpoint routes it (#188 D7)
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// A transport row in a host that registers no transport at all releases the batch and stops
    /// the loop, rather than dead-lettering the queue.
    /// </summary>
    /// <remarks>
    /// The transport's dispatcher is registered with the transport, so its absence is a missing
    /// registration: the rows are fine and dispatch the moment a transport is deployed. Classifying
    /// it as a payload fault would dead-letter every queued contract on the first poll.
    /// </remarks>
    [Fact]
    public async Task ProcessBatch_WhenATransportRowMeetsNoTransport_ReleasesBatchThenRethrows()
    {
        var messages = Enumerable.Range(0, 3).Select(_ => OutboxFixtures.ContractMessage()).ToArray();
        var (store, captured) = StoreReturning(OutboxFixtures.Claim(messages));
        var services = new ServiceCollection();
        services.AddLogging();
        var processor = BuildCore(store, services);

        var fault = await Should.ThrowAsync<OutboxConfigurationException>(
            async () => await processor.ProcessBatchAsync(10, CancellationToken.None));

        fault.Message.ShouldContain("no transport is registered");
        captured.Count.ShouldBe(messages.Length);
        captured.ShouldAllBe(o => o.Kind == OutboxOutcomeKind.Released);
        captured.ShouldAllBe(o => o.RetryCount == 0);
    }

    /// <summary>
    /// A row whose endpoint this build cannot route is dead-lettered on first sight, and only it:
    /// the rest of the batch is dispatched.
    /// </summary>
    [Fact]
    public async Task ProcessBatch_WhenARowsEndpointCannotBeRouted_DeadLettersItAndDispatchesTheRest()
    {
        var unroutable = OutboxFixtures.ContractMessage();
        unroutable.Endpoint = "a-later-endpoint";
        var routable = OutboxFixtures.ContractMessage();
        var transport = new RecordingMessageTransport();
        var (store, captured) = StoreReturning(OutboxFixtures.Claim(unroutable, routable));

        await Build(store, transport).ProcessBatchAsync(10, CancellationToken.None);

        captured.Single(o => o.MessageId == unroutable.Id).Kind.ShouldBe(OutboxOutcomeKind.DeadLetter);
        captured.Single(o => o.MessageId == routable.Id).Kind.ShouldBe(OutboxOutcomeKind.Published);
        transport.Sent.ShouldHaveSingleItem().Id.ShouldBe(routable.Id.Value.ToString());
    }

    // -----------------------------------------------------------------------------------------
    // No transport registered at all
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// A missing <see cref="IMessageTransport"/> registration releases the batch without
    /// rethrowing — it neither stops the worker nor burns the queue's retry budget.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing in <c>TransportOutboxDispatcher</c> implements this. It works because the transport
    /// is a <b>constructor</b> dependency, so the container throws while the processor is
    /// <i>activating</i> the dispatcher — a registered dispatcher that cannot be built, which the
    /// processor classifies batch-wide: every message released, no retry consumed, nothing
    /// rethrown, and <see cref="OutboxBatchAbortReason.DispatcherActivationFailed"/> reported so the
    /// worker backs off and tries again on the next cycle.
    /// </para>
    /// <para>
    /// Move that resolution into <c>DispatchAsync</c> — a one-line refactor that looks equivalent —
    /// and the container's <see cref="InvalidOperationException"/> is thrown from inside a dispatch
    /// instead, where it is classified as transient: every queued message spends its full retry
    /// budget and is then dead-lettered, over a missing line in a composition root. This test is
    /// the tripwire for that change.
    /// </para>
    /// <para>
    /// No assertion on an exception's text. The type the container could not supply is named in the
    /// container's own message, logged with the activation event — text this module does not produce
    /// and must not pin.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ProcessBatch_WhenNoTransportRegistered_SettlesEveryMessageReleasedWithoutRethrowing()
    {
        var messages = Enumerable.Range(0, 3).Select(_ => OutboxFixtures.ContractMessage()).ToArray();
        var claim = OutboxFixtures.Claim(messages);
        var (store, captured) = StoreReturning(claim);

        var processor = BuildWithoutTransport(store);

        // Not rethrown: a registered dispatcher that cannot be built must not stop the worker.
        var result = await processor.ProcessBatchAsync(10, CancellationToken.None);

        result.AbortReason.ShouldBe(OutboxBatchAbortReason.DispatcherActivationFailed);

        // Settled: a stranded lease blocks its row for the whole LockDuration.
        await store.Received(1).ApplyOutcomesAsync(
            claim.Token, Arg.Any<IReadOnlyList<OutboxOutcome>>(), Arg.Any<CancellationToken>());
        captured.Count.ShouldBe(messages.Length);
        captured.ShouldAllBe(o => o.Kind == OutboxOutcomeKind.Released);
    }

    /// <summary>
    /// The operationally important half, stated on its own: nothing was charged a retry.
    /// </summary>
    [Fact]
    public async Task ProcessBatch_WhenNoTransportRegistered_ConsumesNoRetryBudget()
    {
        var message = OutboxFixtures.ContractMessage(retryCount: 2);
        var (store, captured) = StoreReturning(OutboxFixtures.Claim(message));

        await BuildWithoutTransport(store).ProcessBatchAsync(10, CancellationToken.None);

        var outcome = captured.ShouldHaveSingleItem();
        outcome.Kind.ShouldBe(OutboxOutcomeKind.Released);
        outcome.RetryCount.ShouldBe(0, "Released carries no retry count — the row keeps its own");
        outcome.NextRetryAtUtc.ShouldBeNull();
    }

    // -----------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------

    private static OutboxProcessor Build(IOutboxProcessorStore store, IMessageTransport transport)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(transport);
        services.AddKeyedScoped<IOutboxDispatcher, TransportOutboxDispatcher>(OutboxDispatchers.Transport);

        return BuildCore(store, services);
    }

    /// <summary>The dispatcher is registered; its transport is not. The real misconfiguration.</summary>
    private static OutboxProcessor BuildWithoutTransport(IOutboxProcessorStore store)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedScoped<IOutboxDispatcher, TransportOutboxDispatcher>(OutboxDispatchers.Transport);

        return BuildCore(store, services);
    }

    private static OutboxProcessor BuildCore(IOutboxProcessorStore store, ServiceCollection services)
    {
        var provider = services.BuildServiceProvider();

        return new OutboxProcessor(
            store,
            new TestExecutionScopeFactory(provider.GetRequiredService<IServiceScopeFactory>()),
            DefaultOptions,
            new FakeTimeProvider(Now),
            FixedRandom.NoJitter,
            NullLogger<OutboxProcessor>.Instance);
    }

    private static (IOutboxProcessorStore Store, List<OutboxOutcome> Captured) StoreReturning(
        OutboxClaim claim)
    {
        var captured = new List<OutboxOutcome>();
        var store = Substitute.For<IOutboxProcessorStore>();

        store.ClaimBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(claim));

        store.ApplyOutcomesAsync(
                Arg.Any<Guid>(), Arg.Any<IReadOnlyList<OutboxOutcome>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                captured.AddRange(ci.Arg<IReadOnlyList<OutboxOutcome>>());
                return ValueTask.FromResult(captured.Count);
            });

        return (store, captured);
    }
}
