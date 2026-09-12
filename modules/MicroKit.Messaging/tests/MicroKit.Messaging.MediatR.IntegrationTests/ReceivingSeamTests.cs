using Microsoft.Extensions.Logging;

namespace MicroKit.Messaging.MediatR.IntegrationTests;

/// <summary>
/// The round trip, end to end: a command raises a domain event, the notification handler publishes
/// an integration event, the contract row leaves through the transport, and the envelope comes back
/// in through <see cref="IEnvelopeReceiver"/> to become one inbox row per consumer.
/// </summary>
/// <remarks>
/// <para>
/// <b>The producing and consuming halves are genuinely separate here.</b> The only thing crossing
/// between them is a <see cref="MessageEnvelope"/> taken out of the recording transport and handed
/// back to the receiver — no shared type resolution, no producer-side inbox write. That is what
/// makes this a test of the seam rather than of a fan-out wearing a wire's clothes.
/// </para>
/// <para>
/// One SQLite database serves both halves, which a modular monolith is entitled to: the producing
/// outbox and the consuming inbox are different tables, and the contract name — not a CLR type — is
/// what carries the message between them.
/// </para>
/// </remarks>
public sealed class ReceivingSeamTests
{
    private const string Source = "/microkit/e2e";

    /// <summary>
    /// The causal chain, whole, through the wire: a dispatch produces a contract row, the envelope
    /// crosses the seam, the inbox row copies the causation the producer assigned, and the
    /// handler's own work is caused by the delivery itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the assertion ADR-MSG-019 recorded as owed and step 5 could not write: the inbox
    /// half was unit-testable, but "the handler's work names the message that caused it" needs a
    /// message that actually travelled.
    /// </para>
    /// <para>
    /// <b>Copied and derived are asserted separately, and they must be.</b> The row's
    /// <c>CausationId</c> is the notification that caused the contract — copied inbound, one hop
    /// behind. The handler's execution context names the inbox row's own <c>MessageId</c> — derived,
    /// one hop forward. The two are different values here on purpose; an implementation that
    /// confused them would pass if the envelope carried the same value in both places.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnEnvelopeCrossingTheSeam_CausesTheHandlersWorkThroughTheInboxRow()
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

        var outbox = await E2EHarness.ReadOutboxAsync(connection);
        var notification = outbox.Single(m => m.MessageKind == MessageKind.Notification);
        var contract = outbox.Single(m => m.MessageKind == MessageKind.Contract);

        var envelope = transport.Sent.ShouldHaveSingleItem();

        // Nothing was written on the producing side. The in-process fan-out stays deleted, and this
        // is what proves it end to end rather than by reading a dependency graph.
        (await E2EHarness.ReadInboxAsync(connection)).ShouldBeEmpty();

        // ── the seam ──────────────────────────────────────────────────────────────────────────
        var receiver = provider.GetRequiredService<IEnvelopeReceiver>();
        var received = await receiver.ReceiveAsync(envelope);

        received.RowsAdded.ShouldBe(2, "one row per registered consumer");
        received.Duplicates.ShouldBe(0);
        received.ConsumersMatched.ShouldBe(2);

        var inbox = await E2EHarness.ReadInboxAsync(connection);
        inbox.Count.ShouldBe(2);

        inbox.Select(r => r.ConsumerType).Distinct().Count()
            .ShouldBe(2, "the compound dedup key is (MessageId, ConsumerType)");

        foreach (var row in inbox)
        {
            row.MessageId.Value.ShouldBe(
                contract.Id.Value,
                "the row is keyed on the producing row's id, which is what survives a redelivery");

            row.CausationId!.Value.ShouldBe(
                notification.Id.Value,
                "carried inbound: the contract was caused by the notification whose dispatch " +
                "published it, and the receiver copies that rather than reassigning it");

            row.CorrelationId!.Value.ShouldBe(
                notification.CorrelationId.Value,
                "correlation identifies the chain and never advances, not across the wire either");

            row.EventType.ShouldBe(
                typeof(WidgetShipped).AssemblyQualifiedName,
                "resolved from the contract name to THIS process's type — the envelope carries no " +
                "assembly-qualified name, deliberately");

            // The producer's business clock, across the wire and back into storage. Asserted here
            // as well as in the unit test because this is the only place a real provider
            // round-trips the column: the payload is a bare marker since ADR-MSG-018, so if this
            // does not survive, the business time exists nowhere on the receiving side.
            row.OccurredOnUtc.ShouldBe(
                contract.OccurredOnUtc, "carried from the envelope, which carried it from the row");

            row.Status.ShouldBe(InboxMessageStatus.Received);
            row.RetryCount.ShouldBe(0);
            row.TenantId.ShouldBe(envelope.TenantId);
        }

        // ── the drain ─────────────────────────────────────────────────────────────────────────
        await E2EHarness.DrainInboxOnceAsync(provider, CancellationToken.None);

        var drained = await E2EHarness.ReadInboxAsync(connection);
        drained.ShouldAllBe(r => r.Status == InboxMessageStatus.Processed);
        drained.ShouldAllBe(r => r.ClaimToken == null);

        // The assertion the whole test exists for. Every handler ran, and each recorded the
        // causation its execution context carried — which must be the DELIVERED message's id.
        var invocations = recorder.Invocations
            .Where(i => i.HandlerName is nameof(FirstWidgetShippedConsumer)
                                      or nameof(SecondWidgetShippedConsumer))
            .ToList();

        invocations.Count.ShouldBe(2, "both consumers ran — otherwise this proves nothing");

        invocations.ShouldAllBe(i => i.EventId == envelope.MessageId.ToString());

        // Three candidate values, all different, so no copy-through can pass by coincidence:
        // the row's own CausationId names the notification, RowId is a local surrogate, and only
        // MessageId answers "which message caused this" in another process.
        invocations.ShouldAllBe(i => i.EventId != notification.Id.Value.ToString());

        var rowIds = drained.Select(r => r.RowId.ToString()).ToHashSet(StringComparer.Ordinal);
        invocations.ShouldAllBe(i => !rowIds.Contains(i.EventId));
    }

    /// <summary>
    /// A redelivered envelope is absorbed for the consumers already holding a row, and still
    /// delivers to the ones that do not.
    /// </summary>
    /// <remarks>
    /// The five properties the deleted <c>InboxRedeliveryTests</c> proved, now over the receiving
    /// path they belong to: one row per consumer, a duplicate reported rather than thrown,
    /// <c>RetryCount</c> untouched, a duplicate for one consumer not costing the others theirs, and
    /// nothing from MicroKit's own categories logged at <c>Warning</c> or above — because under
    /// at-least-once delivery a redelivery is the nominal path.
    /// </remarks>
    [Fact]
    public async Task Redelivery_ThroughTheSeam_CostsNoConsumerItsRow()
    {
        await using var connection = await E2EHarness.OpenConnectionAsync();
        await E2EHarness.CreateSchemaAsync(connection);

        var recorder = new HandlerInvocationRecorder();
        var logs = new CapturingLoggerProvider();
        var transport = new RecordingMessageTransport();

        await using var provider = BuildProvider(connection, recorder, logs, transport);

        await ShipAWidgetAsync(provider);
        await E2EHarness.DrainOnceAsync(provider, CancellationToken.None);
        await E2EHarness.DrainOnceAsync(provider, CancellationToken.None);

        var envelope = transport.Sent.ShouldHaveSingleItem();
        var receiver = provider.GetRequiredService<IEnvelopeReceiver>();

        (await receiver.ReceiveAsync(envelope)).RowsAdded.ShouldBe(2);

        var mark = logs.Mark();

        // 1. The whole envelope redelivered — an expired broker lease, or an ack that never landed.
        var replay = await receiver.ReceiveAsync(envelope);

        replay.RowsAdded.ShouldBe(0);
        replay.Duplicates.ShouldBe(2, "the unique index held for both consumers");
        replay.ConsumersMatched.ShouldBe(2);

        (await E2EHarness.ReadInboxAsync(connection)).Count
            .ShouldBe(2, "a redelivery writes no second row for a consumer that already has one");

        // 2. A PARTIAL redelivery: one consumer's row is gone — requeued by an operator, or purged
        //    by retention — while the other still holds its own. This is the property an
        //    implementation that returned on the first duplicate would fail, and the only one that
        //    needs a second consumer to be visible at all.
        var survivor = await RemoveOneConsumersRowAsync(connection);

        var partial = await receiver.ReceiveAsync(envelope);

        partial.Duplicates.ShouldBe(1, "the surviving consumer's row was recognised");
        partial.RowsAdded.ShouldBe(
            1, "and the missing one was still written — a duplicate must not end the fan-out");

        var restored = await E2EHarness.ReadInboxAsync(connection);
        restored.Count.ShouldBe(2);
        restored.Select(r => r.ConsumerType).ShouldContain(survivor);

        // 3. No row paid for any of it.
        restored.ShouldAllBe(r => r.RetryCount == 0);
        restored.ShouldAllBe(r => !r.DeadLettered);

        // 4. MicroKit's own categories only. EF Core independently logs the rejected INSERT at
        //    Error on every absorbed duplicate, from a category a library cannot silence: the
        //    setting lives on the consumer's DbContext. That noise is documented rather than
        //    suppressed — what must hold is that MicroKit itself treats a redelivery as nominal.
        logs.Since(mark, LogLevel.Warning)
            .Where(e => e.CategoryName.StartsWith("MicroKit", StringComparison.Ordinal))
            .ShouldBeEmpty(
                "a redelivery is the nominal path under at-least-once delivery, never a fault");

        // 5. And the rows are still drainable — absorbing a duplicate must leave the inbox usable.
        await E2EHarness.DrainInboxOnceAsync(provider, CancellationToken.None);

        (await E2EHarness.ReadInboxAsync(connection))
            .ShouldAllBe(r => r.Status == InboxMessageStatus.Processed);
    }

    private static ServiceProvider BuildProvider(
        SqliteConnection connection,
        HandlerInvocationRecorder recorder,
        CapturingLoggerProvider logs,
        RecordingMessageTransport transport)
        => E2EHarness.BuildProvider(connection, recorder, logs, messaging =>
        {
            messaging.Services.AddSingleton<IMessageTransport>(transport);

            // Publishes<T>() binds the contract name in BOTH directions, so a modular monolith
            // routes its own contracts back to itself with no second declaration. A service that
            // only consumed would call AddIntegrationEventSubscriptions(e => e.Consumes<T>())
            // instead and reach the same registry.
            messaging.Services.AddIntegrationEventContracts(
                Source, e => e.Publishes<WidgetShipped>());

            messaging
                .AddIntegrationEventPublishing()
                .AddEfCoreIntegrationEvents<E2EDbContext>()
                .AddTransportDispatcher()
                .AddMessageHandler<FirstWidgetShippedConsumer, WidgetShipped>()
                .AddMessageHandler<SecondWidgetShippedConsumer, WidgetShipped>();
        });

    private static async Task ShipAWidgetAsync(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var shipped = await mediator.SendCommandAsync<ShipWidgetCommand, Result<Guid>>(
            new ShipWidgetCommand("shipped-widget"));

        shipped.IsSuccess.ShouldBeTrue();
    }

    /// <summary>
    /// Deletes the row of the consumer the receiver reaches SECOND, and returns the consumer type
    /// that keeps its row.
    /// </summary>
    /// <remarks>
    /// Deleting the second one is what makes the assertion sharp: the duplicate is then met
    /// <i>first</i>, so an implementation that returned instead of continuing would never reach the
    /// consumer whose row is missing.
    /// </remarks>
    private static async Task<string> RemoveOneConsumersRowAsync(SqliteConnection connection)
    {
        await using var context = E2EHarness.NewContext(connection);

        var rows = await context.InboxMessages.OrderBy(r => r.ConsumerType).ToListAsync();
        var doomed = rows.Single(r => r.ConsumerType.Contains(
            nameof(SecondWidgetShippedConsumer), StringComparison.Ordinal));

        context.InboxMessages.Remove(doomed);
        await context.SaveChangesAsync();

        return rows.Single(r => r.ConsumerType != doomed.ConsumerType).ConsumerType;
    }
}
