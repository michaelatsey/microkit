using Microsoft.Extensions.Logging;

namespace MicroKit.Messaging.MediatR.IntegrationTests;

/// <summary>
/// Observation test for the cascade path: a notification handler that raises a NEW domain event
/// while running on the post-commit outbox path.
/// </summary>
/// <remarks>
/// This test exists to RECORD behaviour, not to enforce a desired outcome. It asserts what is
/// actually observed and is named for it. No production code is adjusted to make it pass.
/// </remarks>
public sealed class CascadeObservationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Cascade_NotificationHandlerRaisingDomainEvent_SecondDrainProducesNothing()
    {
        await using var connection = await E2EHarness.OpenConnectionAsync();
        await E2EHarness.CreateSchemaAsync(connection);

        var recorder = new HandlerInvocationRecorder();
        var logs = new CapturingLoggerProvider();
        await using var provider = E2EHarness.BuildProvider(connection, recorder, logs);

        await using (var scope = provider.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var inspected = await mediator.SendCommandAsync<InspectWidgetCommand, Result<Guid>>(
                new InspectWidgetCommand("inspected-widget"));

            inspected.IsSuccess.ShouldBeTrue();
        }

        var afterCommand = await E2EHarness.ReadOutboxAsync(connection);
        afterCommand.Count.ShouldBe(1, "the command raises exactly one domain event");

        // ---- drain 1: publishes WidgetInspectedNotification, whose handler raises the cascade ----
        var mark1 = logs.Mark();
        await E2EHarness.DrainOnceAsync(provider, CancellationToken.None);
        var drain1Logs = logs.Since(mark1, LogLevel.Warning);
        var afterDrain1 = await E2EHarness.ReadOutboxAsync(connection);

        // The first-level handler definitely ran and definitely raised the cascade event.
        recorder.For<ArchiveWidgetOnInspectionHandler>().Count.ShouldBe(1);

        // ---- drain 2: would pick up the cascade row, if one had ever been written ----
        var mark2 = logs.Mark();
        await E2EHarness.DrainOnceAsync(provider, CancellationToken.None);
        var drain2Logs = logs.Since(mark2, LogLevel.Warning);
        var afterDrain2 = await E2EHarness.ReadOutboxAsync(connection);

        // ---------------------------------------------------------------------------------------
        // OBSERVED BEHAVIOUR. The cascade row is staged by P4 into the processor scope's DbContext
        // change tracker, and nothing on the outbox path ever calls SaveChanges on that context:
        // TransactionBehavior is the only flush owner and it is not in this path. The staged row
        // dies with the scope, so the cascade domain event is silently lost — no row, no error.
        // ---------------------------------------------------------------------------------------
        afterDrain1.Count.ShouldBe(1, "the cascade row is staged but never flushed — no second row appears");
        afterDrain1.Single().Status.ShouldBe(OutboxMessageStatus.Published);

        afterDrain2.Count.ShouldBe(1, "there was never a second row for the second drain to find");
        afterDrain2.Single().Status.ShouldBe(OutboxMessageStatus.Published);

        // The cascade's own notification handler is therefore never reached.
        recorder.For<RecordWidgetArchivedHandler>().ShouldBeEmpty(
            "WidgetArchivedEvent never reaches the outbox, so its notification is never published");

        // Silent loss: nothing at Warning or above is logged on either drain.
        drain1Logs.ShouldBeEmpty("the loss is silent — nothing warns that the cascade row vanished");
        drain2Logs.ShouldBeEmpty();

        Report(afterCommand.Count, afterDrain1, afterDrain2, drain1Logs, drain2Logs, recorder);
    }

    private void Report(
        int afterCommand,
        List<OutboxMessage> afterDrain1,
        List<OutboxMessage> afterDrain2,
        IReadOnlyList<CapturedLogEntry> drain1Warnings,
        IReadOnlyList<CapturedLogEntry> drain2Warnings,
        HandlerInvocationRecorder recorder)
    {
        output.WriteLine("OBSERVED — cascade");
        output.WriteLine($"  outbox rows after command      : {afterCommand}");
        output.WriteLine($"  outbox rows after drain 1      : {afterDrain1.Count} "
            + $"[{string.Join(", ", afterDrain1.Select(r => r.Status))}]");
        output.WriteLine($"  outbox rows after drain 2      : {afterDrain2.Count} "
            + $"[{string.Join(", ", afterDrain2.Select(r => r.Status))}]");
        output.WriteLine($"  cascade-raising handler ran    : {recorder.For<ArchiveWidgetOnInspectionHandler>().Count} time(s)");
        output.WriteLine($"  cascade notification handler   : {recorder.For<RecordWidgetArchivedHandler>().Count} time(s)");
        output.WriteLine($"  Warning+ during drain 1        : {drain1Warnings.Count}");
        output.WriteLine($"  Warning+ during drain 2        : {drain2Warnings.Count}");
        output.WriteLine("  => the cascade domain event is lost with no row and no diagnostic.");
    }
}
