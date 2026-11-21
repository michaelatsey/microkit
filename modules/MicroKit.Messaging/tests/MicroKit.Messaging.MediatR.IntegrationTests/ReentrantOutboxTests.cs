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
/// The handler publishes through <see cref="IMessageBus"/>: the dispatch scope names its store, so
/// the publication is enlisted there and committed by the handler's own save (#188).
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

        // 1. The command staged one local row and nothing else. The publication has not happened
        //    yet: the handler runs on the drain, not in the command's transaction.
        var staged = await E2EHarness.ReadOutboxAsync(connection);
        var notification = staged.ShouldHaveSingleItem();
        notification.Endpoint.ShouldBe(OutboxMessage.LocalEndpoint);
        notification.ContractName.ShouldBeNull("a local message has no wire identity");

        // 2. The first drain delivers the notification locally; its handler publishes, and the
        //    publication is enlisted in the delivery's store and committed with the handler's row.
        await E2EHarness.DrainOnceAsync(provider, CancellationToken.None);

        var afterFirstDrain = await E2EHarness.ReadOutboxAsync(connection);
        afterFirstDrain.Count.ShouldBe(2, "the dispatch produced a second row — the outbox is reentrant");

        var contract = afterFirstDrain.Single(m => m.Endpoint == null);
        contract.ContractName.ShouldBe(WidgetShipped.ContractName);
        contract.Source.ShouldBe(Source);
        contract.Id.ShouldNotBe(notification.Id, "one table, one primary key column");

        // The causal chain, end to end and through the real container. The notification is a root —
        // it was staged from an HTTP-equivalent command scope with no message above it — and the
        // contract names the row whose dispatch produced it — derived from the row being
        // dispatched, never copied off it, or it would inherit the notification's own (null)
        // causation.
        notification.CausationId.ShouldBeNull(
            "a command-scope publication is a root — nothing above it caused it");

        contract.CausationId.ShouldNotBeNull();
        contract.CausationId!.Value.ShouldBe(
            notification.Id.Value,
            "the cause of the contract is the notification row whose dispatch published it");

        contract.CorrelationId.ShouldBe(
            notification.CorrelationId,
            "correlation identifies the chain and is copied through — only causation advances");

        afterFirstDrain.Single(m => m.Endpoint == OutboxMessage.LocalEndpoint)
            .Status.ShouldBe(OutboxMessageStatus.Published);

        transport.Sent.ShouldBeEmpty("the contract row was staged during the drain, not claimed by it");

        // 3. The second drain claims the contract row and it leaves the process.
        await E2EHarness.DrainOnceAsync(provider, CancellationToken.None);

        var envelope = transport.Sent.ShouldHaveSingleItem();
        envelope.Id.ShouldBe(
            contract.Id.Value.ToString(), "the receiver deduplicates on this, so it must be the row's own id");
        envelope.Type.ShouldBe(WidgetShipped.ContractName);
        envelope.Source.ShouldBe(Source);
        BodyText(envelope).ShouldContain(WidgetIdOf(envelope));

        (await E2EHarness.ReadOutboxAsync(connection))
            .ShouldAllBe(m => m.Status == OutboxMessageStatus.Published);
    }

    private static ServiceProvider BuildProvider(
        SqliteConnection connection,
        HandlerInvocationRecorder recorder,
        CapturingLoggerProvider logs,
        RecordingMessageTransport transport)
        => E2EHarness.BuildProvider(connection, recorder, logs, messaging =>
        {
            // Registered AFTER UseMediatRDomainEvents(): the order is irrelevant, since each row's
            // endpoint names its dispatcher (#188 D7).
            messaging.Services.AddSingleton<IMessageTransport>(transport);
            messaging.ServiceName = Source;
            messaging.Publishes<WidgetShipped>();
            messaging.AddTransportDispatcher();
        });

    private static async Task ShipAWidgetAsync(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var shipped = await mediator.SendCommandAsync<ShipWidgetCommand, Result<Guid>>(
            new ShipWidgetCommand("shipped-widget"));

        shipped.IsSuccess.ShouldBeTrue();
    }

    private static string WidgetIdOf(MessageEnvelope envelope)
        => BodyText(envelope).Split('"').First(p => Guid.TryParse(p, out _));

    private static string BodyText(MessageEnvelope envelope)
        => System.Text.Encoding.UTF8.GetString(envelope.Body.Span);
}
