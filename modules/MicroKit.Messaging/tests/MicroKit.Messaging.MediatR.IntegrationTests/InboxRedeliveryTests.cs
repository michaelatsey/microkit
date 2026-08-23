using Microsoft.Extensions.Logging;

namespace MicroKit.Messaging.MediatR.IntegrationTests;

/// <summary>
/// Outbox redelivery of an integration event whose inbox rows already exist.
/// </summary>
/// <remarks>
/// <para>
/// <b>This test was inverted, not written from scratch, and that matters.</b> It began as an
/// observation test recording the defect: the duplicate inbox insert threw, the exception reached
/// <c>OutboxProcessor</c>, was classified a transient dispatch failure, and retried into the same
/// duplicate until the message dead-lettered — a message that had been delivered correctly on the
/// first attempt.
/// </para>
/// <para>
/// It is the test that caught that defect, so inverted it is the test that stops the regression.
/// Every other assertion in the file is unchanged, including the two that hold either way: the
/// exception never escapes the drain, and the compound key still yields exactly two inbox rows.
/// </para>
/// </remarks>
public sealed class InboxRedeliveryTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Redelivery_AfterInboxRowsWritten_IsDeduplicatedAndTheOutboxRowIsPublished()
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
        // THE CONTRACT.
        //
        // The duplicate (MessageId, ConsumerType) insert still violates the inbox unique index —
        // that index is the dedup gate and nothing about it changed. What changed is who answers
        // for it: EfInboxStore absorbs the violation and reports InboxWriteResult.AlreadyPresent,
        // and InProcessMessagePublisher treats that as a successful skip rather than a failure.
        //
        // So the publisher returns normally, the outbox marks the message Published, and no retry
        // is charged. A redelivery is not an error: under at-least-once delivery it needs no
        // failure at all — one expired lease after a crash is enough to produce it.
        // ---------------------------------------------------------------------------------------
        secondDrain.ShouldBeNull("OutboxProcessor catches dispatch failures — nothing escapes the drain");

        inboxAfterDrain2.Count.ShouldBe(2, "the compound PK prevents duplicate inbox rows");

        var redelivered = outboxAfterDrain2.Single();
        redelivered.Status.ShouldBe(
            OutboxMessageStatus.Published, "a deduplicated redelivery is a successful dispatch");
        redelivered.RetryCount.ShouldBe(0, "no retry may be charged for the nominal path");
        redelivered.DeadLettered.ShouldBeFalse();
        redelivered.ErrorMessage.ShouldBeNullOrEmpty();

        // A redelivery is normal operation, so MicroKit reports it at Debug and counts it as
        // microkit.inbox.messages.deduplicated — never as a warning. Warning-level would drown
        // the log after any incident and train whoever reads it to lower the level, losing the
        // genuine warnings with it. Nothing in the module may report a failure here.
        drain2Warnings
            .Where(e => e.CategoryName.StartsWith("MicroKit.", StringComparison.Ordinal))
            .ShouldBeEmpty("deduplication is not a failure and must not warn");

        // EF Core does log the rejected INSERT at Error, from its own categories, before the
        // store absorbs it. That is outside this library's control — the setting that would
        // silence it lives on the CONSUMER's DbContext — so it is asserted rather than wished
        // away: a redelivery is quiet in MicroKit's logs and noisy in EF's.
        drain2Warnings.ShouldAllBe(
            e => e.CategoryName.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.Ordinal));
        drain2Warnings.ShouldContain(
            e => e.ExceptionTypeName == "DbUpdateException",
            "the unique index is still the dedup gate; what changed is who answers for it");

        // Neither message handler ever runs here, on either drain. DrainOnceAsync drives the OUTBOX
        // coordinator only; IMessageHandler<T> is invoked by the INBOX processor, which this test
        // never drives. The inbox ROWS are the delivery evidence, not the handler invocations.
        recorder.For<FirstWidgetSyncedHandler>().ShouldBeEmpty();
        recorder.For<SecondWidgetSyncedHandler>().ShouldBeEmpty();

        output.WriteLine("Redelivery of an already-consumed integration event");
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
