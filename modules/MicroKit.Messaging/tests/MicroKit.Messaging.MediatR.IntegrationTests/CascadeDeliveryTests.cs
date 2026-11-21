using Microsoft.Extensions.Logging;

namespace MicroKit.Messaging.MediatR.IntegrationTests;

/// <summary>
/// A domain event raised by a notification handler is delivered (#188 D8): the receive middleware
/// dispatches it after the handler and before the engine's save, so its local row commits with the
/// handler's work and the next drain delivers it.
/// </summary>
/// <remarks>
/// It replaces <c>CascadeObservationTests</c>, which pinned the defect this closes: the cascade row
/// was staged on the delivery scope's context, never saved, and reported lost (event 4000).
/// The PostgreSQL proof, with a host that registers no transport at all, is
/// <c>PostgreSql.LocalEndpointTests</c>.
/// </remarks>
public sealed class CascadeDeliveryTests
{
    [Fact]
    public async Task ACascadeEvent_RaisedByANotificationHandler_CommitsWithItsWork_AndIsDeliveredOnce()
    {
        await using var connection = await E2EHarness.OpenConnectionAsync();
        await E2EHarness.CreateSchemaAsync(connection);

        var recorder = new HandlerInvocationRecorder();
        var logs = new CapturingLoggerProvider();
        await using var provider = E2EHarness.BuildProvider(connection, recorder, logs);

        await using (var scope = provider.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<IMediator>()
                .SendCommandAsync<InspectWidgetCommand, Result<Guid>>(new InspectWidgetCommand("inspected")))
                .IsSuccess.ShouldBeTrue();
        }

        // Drain 1 delivers the inspection; its handler raises the archive event.
        var mark = logs.Mark();
        await E2EHarness.DrainOnceAsync(provider, CancellationToken.None);

        recorder.For<ArchiveWidgetOnInspectionHandler>().Count.ShouldBe(1);
        var afterFirst = await E2EHarness.ReadOutboxAsync(connection);
        afterFirst.Count.ShouldBe(2, "the cascade row committed with the handler's work");
        var cascade = afterFirst.Single(r => r.EventType.Contains(nameof(WidgetArchivedNotification), StringComparison.Ordinal));
        cascade.Endpoint.ShouldBe(OutboxMessage.LocalEndpoint);
        cascade.Status.ShouldBe(OutboxMessageStatus.Pending);

        await using (var probe = E2EHarness.NewContext(connection))
        {
            (await probe.Aggregates.AsNoTracking().CountAsync(a => a.Name == "archived"))
                .ShouldBe(1, "the handler's own write committed in the same transaction");
        }

        // Drain 2 delivers the cascade, once; drain 3 finds nothing to do.
        await E2EHarness.DrainOnceAsync(provider, CancellationToken.None);
        await E2EHarness.DrainOnceAsync(provider, CancellationToken.None);

        recorder.For<RecordWidgetArchivedHandler>().Count.ShouldBe(1);
        recorder.For<ArchiveWidgetOnInspectionHandler>().Count.ShouldBe(1, "a delivered handler is not run again");
        (await E2EHarness.ReadOutboxAsync(connection)).ShouldAllBe(r => r.Status == OutboxMessageStatus.Published);
        (await E2EHarness.ReadInboxAsync(connection)).ShouldAllBe(r => r.Status == InboxMessageStatus.Processed);
        logs.Since(mark, LogLevel.Error).Where(e => e.EventId == 4000).ShouldBeEmpty("nothing was left unsaved");
    }
}
