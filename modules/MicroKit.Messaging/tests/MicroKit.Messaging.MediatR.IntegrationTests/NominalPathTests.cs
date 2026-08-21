namespace MicroKit.Messaging.MediatR.IntegrationTests;

/// <summary>
/// The first test in the monorepo to run the whole domain-event → outbox → notification path as one
/// system: TransactionBehavior (order 700) → DomainEventsDispatcher P1–P4 → EfOutboxStore →
/// OutboxProcessor → MediatROutboxDispatcher → INotificationHandler.
/// </summary>
/// <remarks>
/// Assertions read observable state only — rows through a verification <c>DbContext</c> built
/// outside the container, and the DI recorder. No <c>IOutboxProcessorStore</c> / <c>IOutboxWriter</c>
/// method is asserted on: that surface is being rewritten and such assertions would not survive it.
/// </remarks>
public sealed class NominalPathTests
{
    [Fact]
    public async Task NominalPath_CommandProducesOutboxRow_DrainInvokesNotificationHandler()
    {
        await using var connection = await E2EHarness.OpenConnectionAsync();
        await E2EHarness.CreateSchemaAsync(connection);

        var recorder = new HandlerInvocationRecorder();
        var logs = new CapturingLoggerProvider();
        await using var provider = E2EHarness.BuildProvider(connection, recorder, logs);

        await using (var scope = provider.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var created = await mediator.SendCommandAsync<CreateWidgetCommand, Result<Guid>>(
                new CreateWidgetCommand("first-widget"));

            created.IsSuccess.ShouldBeTrue();
        }

        // 1. The command committed exactly one outbox row, in the same transaction as the aggregate.
        var staged = await E2EHarness.ReadOutboxAsync(connection);
        staged.Count.ShouldBe(1, "the single domain event must produce exactly one outbox row");
        staged[0].Status.ShouldBe(OutboxMessageStatus.Pending);

        // 2. Nothing has fanned out yet. The notification is staged for the outbox, not published
        //    in-transaction — publishing here as well would double-execute every handler.
        recorder.Invocations.ShouldBeEmpty(
            "the notification must not be published until the outbox is drained");

        // 3. One drain publishes the notification exactly once.
        await E2EHarness.DrainOnceAsync(provider, CancellationToken.None);

        recorder.Invocations.Count.ShouldBe(1);
        recorder.Invocations[0].HandlerName.ShouldBe(nameof(RecordWidgetCreatedHandler));
        // The outbox row's identity IS the domain event's EventId — OutboxMessageFactory stamps it
        // so the row and the eventual inbox dedup key share one end-to-end identity.
        recorder.Invocations[0].EventId.ShouldBe(staged[0].Id.Value.ToString());

        // 4. The row reached the terminal success state.
        var afterDrain = await E2EHarness.ReadOutboxAsync(connection);
        afterDrain.Count.ShouldBe(1);
        afterDrain[0].Status.ShouldBe(OutboxMessageStatus.Published);
    }
}
