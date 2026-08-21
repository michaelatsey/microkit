namespace MicroKit.Messaging.MediatR.IntegrationTests;

/// <summary>
/// Pins the sink seam end to end (ADR-MEDIATR-014): the outbox rows that used to be written by the
/// glue's own <c>IDomainEventsDispatcher</c> are now written by an <c>IDomainEventSink</c> that the
/// core orchestrator invokes. Both callers of <c>DispatchEventsAsync</c> must reach it.
/// </summary>
/// <remarks>
/// These are the tests the new sensitivity standard names: <b>delete the sink loop from the
/// orchestrator and both must fail.</b> The first covers the in-pipeline caller
/// (<c>TransactionBehavior</c>, order 700); the second covers the out-of-pipeline caller
/// (<c>DomainEventsCascadeNotificationPublisher</c>, ADR-MSG-013) — which ADR-MEDIATR-014 flags as
/// the one place a careless implementation would break silently.
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
    }

    [Fact]
    public async Task CascadePublish_WhenHandlerRaisesEvent_StagesOutboxRowInProcessorScope()
    {
        await using var connection = await E2EHarness.OpenConnectionAsync();
        await E2EHarness.CreateSchemaAsync(connection);

        var recorder = new HandlerInvocationRecorder();
        var logs = new CapturingLoggerProvider();
        await using var provider = E2EHarness.BuildProvider(connection, recorder, logs);

        await using var scope = provider.CreateAsyncScope();

        // Publishing the notification directly is the outbox processor's own step, minus the drain:
        // DomainEventsCascadeNotificationPublisher runs every handler and then calls
        // IDomainEventsDispatcher.DispatchEventsAsync once. ArchiveWidgetOnInspectionHandler raises
        // WidgetArchivedEvent on a newly tracked aggregate while it runs.
        var aggregateId = Guid.NewGuid();
        await scope.ServiceProvider.GetRequiredService<IPublisher>()
            .Publish(new WidgetInspectedNotification(new WidgetInspectedEvent(aggregateId)));

        recorder.For<ArchiveWidgetOnInspectionHandler>().Count.ShouldBe(1);

        // STAGED, not persisted. Nothing on the outbox processing path calls SaveChanges —
        // TransactionBehavior is the sole flush owner (ADR-MSG-012) and is not in this path, so the
        // staged row dies with the scope. That is L0-FINDINGS.md Finding #3, still open and out of
        // scope here; CascadeObservationTests pins the resulting loss. What THIS test pins is the
        // half that ADR-MEDIATR-014 owns: the cascade dispatch still reaches the sinks, so the row
        // is produced at all. Delete the orchestrator's sink loop and the change tracker is empty.
        var context = scope.ServiceProvider.GetRequiredService<E2EDbContext>();
        var staged = context.ChangeTracker.Entries<OutboxMessage>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .ToList();

        staged.Count.ShouldBe(1, "the cascade event must reach the outbox sink");
        staged[0].EventType.ShouldContain(nameof(WidgetArchivedNotification));
    }
}
