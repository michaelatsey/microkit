namespace MicroKit.Messaging.MediatR.IntegrationTests;

/// <summary>
/// Pins the sink seam end to end (ADR-MEDIATR-014): the outbox rows that used to be written by the
/// glue's own <c>IDomainEventsDispatcher</c> are now written by an <c>IDomainEventsSink</c> that the
/// core orchestrator invokes. Both callers of <c>DispatchEventsAsync</c> must reach it.
/// </summary>
/// <remarks>
/// <b>Delete the sink loop from the orchestrator and this fails.</b> It covers the in-pipeline caller
/// (<c>TransactionBehavior</c>, order 700). The other caller — the dispatch after a handler, now the
/// receive middleware of #188 D8 — is covered by <c>CascadeDeliveryTests</c>, which asserts the
/// cascade is delivered rather than merely staged.
/// </remarks>
public sealed class DomainEventSinkStagingTests
{
    [Fact]
    public async Task Command_WhenMappedEventRaised_StagesOutboxRowInSameTransaction()
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
                new CreateWidgetCommand("sink-staged-widget"));

            created.IsSuccess.ShouldBeTrue();
        }

        // Read through a fresh context: the row is COMMITTED, not merely tracked — which is what
        // "in the same transaction as the aggregate" means. Delete the orchestrator's sink loop and
        // this is zero.
        var rows = await E2EHarness.ReadOutboxAsync(connection);
        rows.Count.ShouldBe(1);

        // And it came from the notification path (P3), not from a raw domain event.
        rows[0].EventType.ShouldContain(nameof(WidgetCreatedNotification));

        // A local message (#188 D7): drained into the receive pipeline, never handed to a transport.
        rows[0].Endpoint.ShouldBe(OutboxMessage.LocalEndpoint);
    }
}
