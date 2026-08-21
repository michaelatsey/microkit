using Microsoft.Extensions.Logging;

namespace MicroKit.Messaging.MediatR.IntegrationTests;

/// <summary>
/// Observation test for outbox redelivery of an integration event that already produced inbox rows.
/// </summary>
/// <remarks>
/// This test exists to RECORD behaviour, not to enforce a desired outcome. It asserts what is
/// actually observed. No production code is adjusted to make it pass.
/// </remarks>
public sealed class InboxDuplicateObservationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Redelivery_AfterInboxRowsWritten_SecondDispatchBehaviour()
    {
        await using var connection = await E2EHarness.OpenConnectionAsync();
        await E2EHarness.CreateSchemaAsync(connection);

        var recorder = new HandlerInvocationRecorder();
        var logs = new CapturingLoggerProvider();

        // TWO consumers for one event => InProcessMessagePublisher writes two InboxMessage rows,
        // one per ConsumerType, the compound PK being (MessageId, ConsumerType).
        await using var provider = E2EHarness.BuildProvider(connection, recorder, logs, messaging =>
            messaging
                .AddMessageHandler<FirstWidgetSyncedHandler, WidgetSyncedEvent>()
                .AddMessageHandler<SecondWidgetSyncedHandler, WidgetSyncedEvent>());

        await using (var scope = provider.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var synced = await mediator.SendCommandAsync<SyncWidgetCommand, Result<Guid>>(
                new SyncWidgetCommand("sync-payload"));

            synced.IsSuccess.ShouldBeTrue();
        }

        (await E2EHarness.ReadOutboxAsync(connection)).Count.ShouldBe(1);

        // ---- drain 1: the integration event fans out to both consumers' inbox rows ----
        var mark1 = logs.Mark();
        await E2EHarness.DrainOnceAsync(provider, CancellationToken.None);
        var drain1Warnings = logs.Since(mark1, LogLevel.Warning);

        var inboxAfterDrain1 = await E2EHarness.ReadInboxAsync(connection);
        var outboxAfterDrain1 = await E2EHarness.ReadOutboxAsync(connection);

        inboxAfterDrain1.Count.ShouldBe(2, "one inbox row per registered consumer");
        inboxAfterDrain1.Select(r => r.ConsumerType).Distinct().Count().ShouldBe(2);
        outboxAfterDrain1.Single().Status.ShouldBe(OutboxMessageStatus.Published);
        drain1Warnings.ShouldBeEmpty("the first dispatch is the happy path");

        // ---- force redelivery of the SAME outbox row ----
        await ForceRedeliveryAsync(connection);

        // ---- drain 2: the same message dispatches again against an inbox that already has its rows ----
        var mark2 = logs.Mark();
        var secondDrain = await Record.ExceptionAsync(
            () => E2EHarness.DrainOnceAsync(provider, CancellationToken.None));
        var drain2Warnings = logs.Since(mark2, LogLevel.Warning);

        var inboxAfterDrain2 = await E2EHarness.ReadInboxAsync(connection);
        var outboxAfterDrain2 = await E2EHarness.ReadOutboxAsync(connection);

        // ---------------------------------------------------------------------------------------
        // OBSERVED BEHAVIOUR — recorded, not prescribed.
        //
        // The duplicate (MessageId, ConsumerType) insert violates the inbox compound PK.
        // OutboxProcessor wraps dispatch in try/catch, so the exception never escapes the drain:
        // it is converted into a retry decision on the OUTBOX row and reported only via a log line.
        // The inbox is unchanged; the outbox row goes back to Pending with RetryCount incremented.
        // ---------------------------------------------------------------------------------------
        secondDrain.ShouldBeNull("OutboxProcessor catches dispatch failures — nothing escapes the drain");

        inboxAfterDrain2.Count.ShouldBe(2, "the compound PK prevents duplicate inbox rows");

        var redelivered = outboxAfterDrain2.Single();
        redelivered.Status.ShouldBe(OutboxMessageStatus.Pending);
        redelivered.RetryCount.ShouldBe(1);
        redelivered.DeadLettered.ShouldBeFalse();
        redelivered.ErrorMessage.ShouldNotBeNullOrEmpty();

        // The log line is the ONLY place the failure is visible: the row state alone cannot
        // distinguish "the store absorbed the duplicate" from "the exception escaped and was caught".
        drain2Warnings.ShouldNotBeEmpty("the retry decision must be reported somewhere");
        drain2Warnings.ShouldContain(e => e.ExceptionTypeName == "DbUpdateException");

        // Neither message handler ever runs here, on either drain. DrainOnceAsync drives the OUTBOX
        // coordinator only; IMessageHandler<T> is invoked by the INBOX processor, which this test
        // never drives. The inbox ROWS are the delivery evidence, not the handler invocations.
        recorder.For<FirstWidgetSyncedHandler>().ShouldBeEmpty();
        recorder.For<SecondWidgetSyncedHandler>().ShouldBeEmpty();

        output.WriteLine("OBSERVED — redelivery of an already-published integration event");
        output.WriteLine($"  exception escaping drain 2 : {secondDrain?.GetType().Name ?? "(none)"}");
        output.WriteLine($"  inbox rows after drain 1   : {inboxAfterDrain1.Count}");
        output.WriteLine($"  inbox rows after drain 2   : {inboxAfterDrain2.Count}");
        output.WriteLine($"  outbox status after drain 2: {redelivered.Status}");
        output.WriteLine($"  outbox RetryCount          : {redelivered.RetryCount}");
        output.WriteLine($"  outbox DeadLettered        : {redelivered.DeadLettered}");
        output.WriteLine($"  outbox ErrorMessage        : {redelivered.ErrorMessage}");
        output.WriteLine($"  Warning+ during drain 2    : {drain2Warnings.Count}");
        foreach (var entry in drain2Warnings)
            output.WriteLine($"    {entry}");
    }

    /// <summary>
    /// Forces redelivery of an already-published outbox row by direct row manipulation through the
    /// verification DbContext.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT a store call: <c>IOutboxProcessorStore</c> is being rewritten, and an
    /// assertion or a setup step routed through it would not survive that change. Everything the
    /// eligibility filter in <c>GetPendingAsync</c> looks at must be reset — Status, LockedUntilUtc
    /// and NextRetryAtUtc — plus ProcessedAtUtc for cleanliness. This is the single place that
    /// touches outbox columns directly; adapt it here when the store changes.
    /// </remarks>
    private static async Task ForceRedeliveryAsync(SqliteConnection connection)
    {
        await using var context = E2EHarness.NewContext(connection);

        var row = await context.OutboxMessages.SingleAsync();
        row.Status = OutboxMessageStatus.Pending;
        row.LockedUntilUtc = null;
        row.NextRetryAtUtc = null;
        row.ProcessedAtUtc = null;

        await context.SaveChangesAsync();
    }
}
