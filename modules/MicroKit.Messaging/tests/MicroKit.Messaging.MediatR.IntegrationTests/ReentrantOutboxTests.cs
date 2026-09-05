using Microsoft.Extensions.Logging;

namespace MicroKit.Messaging.MediatR.IntegrationTests;

/// <summary>
/// The chain this step exists to close, driven end to end: a command raises a domain event, the
/// drain fans it out, a notification handler publishes an integration event, and the second pass
/// through the same queue hands it to a transport.
/// </summary>
/// <remarks>
/// <para>
/// This is the reentrant outbox of ADR-MSG-019 as one system — one table, two natures of row, the
/// second produced by dispatching the first. Nothing here is substituted except
/// <see cref="IMessageTransport"/>, which has no implementation in MicroKit by design.
/// </para>
/// <para>
/// It is also the test ADR-MSG-019 recorded as owed. <c>InboxRedeliveryTests</c> was deleted with
/// the in-process fan-out, and its central property — a redelivered dispatch produces no duplicate
/// and no dead-letter — was left uncovered. <see cref="Redelivery_ProducesNoDuplicateContract"/>
/// restores it on the producing side, where the replay key now enforces it.
/// </para>
/// </remarks>
public sealed class ReentrantOutboxTests
{
    private const string Source = "/microkit/e2e";

    [Fact]
    public async Task ANotificationHandlersPublication_BecomesAContractRow_AndReachesTheTransport()
    {
        await using var connection = await E2EHarness.OpenConnectionAsync();
        await E2EHarness.CreateSchemaAsync(connection);

        var recorder = new HandlerInvocationRecorder();
        var logs = new CapturingLoggerProvider();
        var transport = new RecordingMessageTransport();

        await using var provider = BuildProvider(connection, recorder, logs, transport);

        await ShipAWidgetAsync(provider);

        // 1. The command staged one Notification row and nothing else. The publication has not
        //    happened yet: the handler runs on the drain, not in the command's transaction.
        var staged = await E2EHarness.ReadOutboxAsync(connection);
        var notification = staged.ShouldHaveSingleItem();
        notification.MessageKind.ShouldBe(MessageKind.Notification);
        notification.ContractName.ShouldBeNull("a notification has no wire identity");

        // 2. The first drain fans the notification out; its handler publishes.
        await E2EHarness.DrainOnceAsync(provider, CancellationToken.None);

        var afterFirstDrain = await E2EHarness.ReadOutboxAsync(connection);
        afterFirstDrain.Count.ShouldBe(2, "the dispatch produced a second row — the outbox is reentrant");

        var contract = afterFirstDrain.Single(m => m.MessageKind == MessageKind.Contract);
        contract.ContractName.ShouldBe(WidgetShipped.ContractName);
        contract.Source.ShouldBe(Source);
        contract.OriginMessageId.ShouldBe(
            notification.Id, "the contract must name the dispatch that produced it");
        contract.Id.ShouldNotBe(notification.Id, "one table, one primary key column");

        // The causal chain, end to end and through the real container. The notification is a root —
        // it was staged from an HTTP-equivalent command scope with no message above it — and the
        // contract names the row whose dispatch produced it.
        //
        // Two identities, two mechanisms, deliberately not merged: OriginMessageId is a REPLAY KEY
        // and may never degrade, CausationId is a TRACE LINK and degrades to null when unparseable.
        // They coincide here, which is exactly why the second assertion is worth making — before
        // this, CausationId was copied off the row being dispatched rather than derived from it, so
        // it inherited the notification's own (null) causation and the chain was null on every row
        // of every path. Nothing in the suite noticed, because every other causation test feeds the
        // value in through a stubbed IExecutionContext.
        notification.CausationId.ShouldBeNull(
            "a command-scope publication is a root — nothing above it caused it");

        contract.CausationId.ShouldNotBeNull();
        contract.CausationId!.Value.ShouldBe(
            notification.Id.Value,
            "the cause of the contract is the notification row whose dispatch published it");

        contract.CorrelationId.ShouldBe(
            notification.CorrelationId,
            "correlation identifies the chain and is copied through — only causation advances");

        afterFirstDrain.Single(m => m.MessageKind == MessageKind.Notification)
            .Status.ShouldBe(OutboxMessageStatus.Published);

        transport.Sent.ShouldBeEmpty("the contract row was staged during the drain, not claimed by it");

        // 3. The second drain claims the contract row and it leaves the process.
        await E2EHarness.DrainOnceAsync(provider, CancellationToken.None);

        var envelope = transport.Sent.ShouldHaveSingleItem();
        envelope.MessageId.ShouldBe(
            contract.Id.Value, "the receiver deduplicates on this, so it must be the row's own id");
        envelope.ContractName.ShouldBe(WidgetShipped.ContractName);
        envelope.Source.ShouldBe(Source);
        envelope.Payload.ShouldContain(WidgetIdOf(envelope));

        (await E2EHarness.ReadOutboxAsync(connection))
            .ShouldAllBe(m => m.Status == OutboxMessageStatus.Published);
    }

    /// <summary>
    /// A redelivered dispatch re-runs its handler, which republishes the same contract from the
    /// same origin — and the replay key absorbs it.
    /// </summary>
    /// <remarks>
    /// The property the deleted <c>InboxRedeliveryTests</c> existed for: no duplicate, no retry
    /// consumed, no dead-letter, and nothing logged at <c>Warning</c> or above, because a
    /// redelivery is nominal rather than a fault. Here it is the producing side that enforces it,
    /// through <c>UX_OutboxMessages_Origin_ContractName</c> instead of the consumer's inbox.
    /// </remarks>
    [Fact]
    public async Task Redelivery_ProducesNoDuplicateContract()
    {
        await using var connection = await E2EHarness.OpenConnectionAsync();
        await E2EHarness.CreateSchemaAsync(connection);

        var recorder = new HandlerInvocationRecorder();
        var logs = new CapturingLoggerProvider();
        var transport = new RecordingMessageTransport();

        await using var provider = BuildProvider(connection, recorder, logs, transport);

        await ShipAWidgetAsync(provider);
        await E2EHarness.DrainOnceAsync(provider, CancellationToken.None);   // publishes
        await E2EHarness.DrainOnceAsync(provider, CancellationToken.None);   // sends

        var beforeReplay = await E2EHarness.ReadOutboxAsync(connection);
        beforeReplay.Count.ShouldBe(2);

        // Force the redelivery a crash between dispatch and settlement would produce: the
        // notification row goes back to Pending with its lease cleared, exactly as an expired
        // lease leaves it.
        var mark = logs.Mark();
        await RequeueNotificationAsync(connection);

        await E2EHarness.DrainOnceAsync(provider, CancellationToken.None);

        var afterReplay = await E2EHarness.ReadOutboxAsync(connection);

        afterReplay.Count.ShouldBe(
            2, "the republished contract collided on the replay key and was absorbed");
        afterReplay.Count(m => m.MessageKind == MessageKind.Contract).ShouldBe(1);

        var notification = afterReplay.Single(m => m.MessageKind == MessageKind.Notification);
        notification.Status.ShouldBe(
            OutboxMessageStatus.Published, "an absorbed replay is a success, not a failure");
        notification.RetryCount.ShouldBe(
            0, "the regression this guards is a correctly delivered message retried to death");
        notification.DeadLettered.ShouldBeFalse();

        transport.Sent.Count.ShouldBe(1, "no second contract row means nothing more to send");

        recorder.Invocations.Count(i => i.HandlerName == nameof(PublishWidgetShippedHandler))
            .ShouldBe(2, "the handler DID re-run — otherwise this proves nothing about the key");

        // MicroKit's own categories only. EF Core independently logs the rejected INSERT at Error
        // on every absorbed replay, and a library cannot silence it: the setting lives on the
        // CONSUMER's DbContext. That noise is documented rather than suppressed — asserting over
        // it would either fail here forever or force the assertion down to a level that proves
        // nothing. What must hold is that MicroKit itself treats the replay as nominal.
        logs.Since(mark, LogLevel.Warning)
            .Where(e => e.CategoryName.StartsWith("MicroKit", StringComparison.Ordinal))
            .ShouldBeEmpty(
                "a redelivery is the nominal path under at-least-once delivery, never a fault");
    }

    private static ServiceProvider BuildProvider(
        SqliteConnection connection,
        HandlerInvocationRecorder recorder,
        CapturingLoggerProvider logs,
        RecordingMessageTransport transport)
        => E2EHarness.BuildProvider(connection, recorder, logs, messaging =>
        {
            // Registered AFTER AddMediatRDomainEvents(), which is the order ADR-MSG-019's keyed
            // seam exists to make irrelevant: the glue holds the unkeyed slot and resolves its
            // inner through OutboxDispatcherKeys.Standard, so the transport dispatcher still runs.
            messaging.Services.AddSingleton<IMessageTransport>(transport);
            messaging.Services.AddIntegrationEventContracts(
                Source, e => e.Publishes<WidgetShipped>());

            messaging
                .AddIntegrationEventPublishing()
                .AddEfCoreIntegrationEvents<E2EDbContext>()
                .AddTransportDispatcher();
        });

    private static async Task ShipAWidgetAsync(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var shipped = await mediator.SendCommandAsync<ShipWidgetCommand, Result<Guid>>(
            new ShipWidgetCommand("shipped-widget"));

        shipped.IsSuccess.ShouldBeTrue();
    }

    /// <summary>Puts the notification row back where an expired lease would leave it.</summary>
    private static async Task RequeueNotificationAsync(SqliteConnection connection)
    {
        await using var context = E2EHarness.NewContext(connection);

        var row = await context.OutboxMessages
            .SingleAsync(m => m.MessageKind == MessageKind.Notification);

        row.Status = OutboxMessageStatus.Pending;
        row.ProcessedAtUtc = null;
        row.LockedUntilUtc = null;
        row.ClaimToken = null;

        await context.SaveChangesAsync();
    }

    private static string WidgetIdOf(MessageEnvelope envelope)
        => envelope.Payload.Split('"').First(p => Guid.TryParse(p, out _));
}
