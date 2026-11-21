using MicroKit.Messaging.IntegrationTests.PostgreSql;

namespace MicroKit.Messaging.MediatR.IntegrationTests.PostgreSql;

/// <summary>
/// Domain-event notifications as local messages, on PostgreSQL, in a host that registers no
/// transport at all (#188 D7, D8): V2 for MediatR handlers and V3.
/// </summary>
[Collection(PostgreSqlSuite.Name)]
public sealed class LocalEndpointTests(PostgreSqlFixture fixture)
{
    /// <summary>
    /// V2, MediatR handlers. Two handlers of one notification, one failing: the other runs exactly
    /// once; the failing one rolls back what it saved, is retried from its own inbox row and ends
    /// <c>Processed</c>. A redelivery of the notification while that row is <c>Failed</c> runs
    /// neither handler, and the retry then runs exactly once.
    /// </summary>
    [DockerRequiredFact]
    public async Task TwoHandlers_OneFailing_TheOtherRunsOnce_AndTheFailingOneIsRetriedFromItsRow()
    {
        await using var host = await PgHost.StartAsync(fixture.ConnectionString);
        host.Services.GetService<IMessageTransport>().ShouldBeNull("no transport at all");
        host.Probe.Failures[PgReceiptHandler.Name] = 1;
        var order = Guid.NewGuid();

        await host.SendAsync(new PgSettleOrderCommand(order));
        var row = (await host.OutboxAsync()).ShouldHaveSingleItem();
        row.Endpoint.ShouldBe(OutboxMessage.LocalEndpoint);

        // The delivery: the ledger commits with its row; the receipt saved, then threw.
        await host.DrainAsync();

        host.Probe.Calls(PgLedgerHandler.Name).ShouldBe(1);
        host.Probe.Calls(PgReceiptHandler.Name).ShouldBe(1);
        (await host.EffectsAsync()).ShouldBe([$"ledger:{order}"], "what the failing handler saved rolled back");
        var inbox = await host.InboxAsync();
        inbox.Count.ShouldBe(2, "one row per handler, keyed on the notification's id");
        inbox.ShouldAllBe(r => r.MessageId == row.Id);
        inbox.Single(r => r.ConsumerType.Contains(nameof(PgLedgerHandler), StringComparison.Ordinal))
            .Status.ShouldBe(InboxMessageStatus.Processed);
        var failed = inbox.Single(r => r.ConsumerType.Contains(nameof(PgReceiptHandler), StringComparison.Ordinal));
        failed.Status.ShouldBe(InboxMessageStatus.Failed);
        failed.RetryCount.ShouldBe(1);
        (await host.OutboxAsync()).Single().Status.ShouldBe(OutboxMessageStatus.Published, "the failure is the inbox row's now");

        // A redelivery while the row is Failed: a dispatch replayed after a crash before settlement.
        await host.ReplayAsync(row.Id);
        await host.DrainAsync();

        host.Probe.Calls(PgLedgerHandler.Name).ShouldBe(1, "its row is Processed: skipped");
        host.Probe.Calls(PgReceiptHandler.Name).ShouldBe(1, "its row is Failed: a redelivery never starts a second attempt");

        // The store's retry loop owns the failure.
        await host.RetryAsync();
        await host.RetryAsync();

        host.Probe.Calls(PgReceiptHandler.Name).ShouldBe(2, "retried exactly once");
        host.Probe.Calls(PgLedgerHandler.Name).ShouldBe(1);
        (await host.EffectsAsync()).Order(StringComparer.Ordinal).ShouldBe([$"ledger:{order}", $"receipt:{order}"]);
        (await host.InboxAsync()).ShouldAllBe(r => r.Status == InboxMessageStatus.Processed);
    }

    /// <summary>
    /// V3, MediatR handler. A domain event raised by a notification handler commits with its work and
    /// is delivered, once — in a host with no transport.
    /// </summary>
    [DockerRequiredFact]
    public async Task ACascadeRaisedByANotificationHandler_IsDeliveredOnce_WithNoTransport()
    {
        await using var host = await PgHost.StartAsync(fixture.ConnectionString);
        host.Services.GetService<IMessageTransport>().ShouldBeNull("no transport at all");
        var parcel = Guid.NewGuid();

        await host.SendAsync(new PgInspectParcelCommand(parcel));
        await host.DrainAsync();   // the inspection; its handler raises the archive event

        var outbox = await host.OutboxAsync();
        outbox.Count.ShouldBe(2, "the cascade row committed with the handler's work");
        outbox.ShouldAllBe(r => r.Endpoint == OutboxMessage.LocalEndpoint);

        await host.DrainAsync();   // the cascade
        await host.DrainAsync();   // nothing left

        host.Probe.Calls(PgArchiveOnInspection.Name).ShouldBe(1);
        host.Probe.Calls(PgRecordArchived.Name).ShouldBe(1);
        (await host.EffectsAsync()).ShouldBe([$"record-archived:{parcel}"]);
        (await host.OutboxAsync()).ShouldAllBe(r => r.Status == OutboxMessageStatus.Published);
        host.Logs.Since(0).Where(e => e.EventId == 4000).ShouldBeEmpty("nothing was left unsaved");
    }

    /// <summary>
    /// V3, bus handler. A domain event raised by an <see cref="IMessageHandler{TMessage}"/> — the
    /// message arriving through the receiving seam, with no transport registered — commits with the
    /// handler's row and is delivered to its notification handler, once; a redelivered envelope runs
    /// nothing again.
    /// </summary>
    [DockerRequiredFact]
    public async Task ACascadeRaisedByABusHandler_IsDeliveredOnce_AndARedeliveryRunsNothing()
    {
        await using var host = await PgHost.StartAsync(fixture.ConnectionString, bus: b => b.AddHandler<PgPaymentHandler>());
        host.Services.GetService<IMessageTransport>().ShouldBeNull("no transport at all");
        var parcel = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        (await host.ReceiveAsync(messageId, new PgPaymentReceived(parcel))).Disposition.ShouldBe(ReceiveDisposition.Acknowledge);
        var cascade = (await host.OutboxAsync()).ShouldHaveSingleItem("the bus handler's cascade committed with its row");
        cascade.Endpoint.ShouldBe(OutboxMessage.LocalEndpoint);

        (await host.ReceiveAsync(messageId, new PgPaymentReceived(parcel))).Disposition.ShouldBe(ReceiveDisposition.Acknowledge);
        await host.DrainAsync();
        await host.DrainAsync();

        host.Probe.Calls(PgPaymentHandler.Name).ShouldBe(1, "the redelivered envelope was skipped on its row");
        host.Probe.Calls(PgRecordArchived.Name).ShouldBe(1);
        (await host.OutboxAsync()).ShouldHaveSingleItem("no second cascade from the redelivery");
        (await host.EffectsAsync()).ShouldBe([$"record-archived:{parcel}"]);
    }
}
