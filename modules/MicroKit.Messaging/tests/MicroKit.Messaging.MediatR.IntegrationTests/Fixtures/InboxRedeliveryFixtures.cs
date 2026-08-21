using MicroKit.Execution.Abstractions;
using MicroKit.Messaging.Outbox;
using static MicroKit.Result.Result;

namespace MicroKit.Messaging.MediatR.IntegrationTests.Fixtures;

/// <summary>
/// An integration event — NOT a domain-event notification. This matters for routing:
/// MediatROutboxDispatcher checks <c>payload is INotification</c> first and only then delegates to
/// the Core dispatcher, so this payload takes the integration-event branch and reaches
/// InProcessMessagePublisher, which writes one InboxMessage per registered consumer.
/// </summary>
internal sealed record WidgetSyncedEvent : IIntegrationEvent
{
    public MessageId MessageId { get; init; } = MessageId.New();

    // Never null or empty in fixtures — messaging rule 3.
    public string TenantId { get; init; } = "tenant-e2e";

    public CorrelationId? CorrelationId { get; init; }

    public CausationId? CausationId { get; init; }

    public DateTimeOffset OccurredOnUtc { get; init; } = DateTimeOffset.UtcNow;

    public string Payload { get; init; } = string.Empty;
}

/// <summary>First consumer. The inbox dedup key is (MessageId, ConsumerType), so each handler gets its own row.</summary>
internal sealed class FirstWidgetSyncedHandler(HandlerInvocationRecorder recorder)
    : IMessageHandler<WidgetSyncedEvent>
{
    public ValueTask HandleAsync(WidgetSyncedEvent evt, CancellationToken ct = default)
    {
        recorder.Record(nameof(FirstWidgetSyncedHandler), evt.MessageId.Value.ToString());
        return ValueTask.CompletedTask;
    }
}

/// <summary>Second consumer of the same event — this is what makes the drain write TWO inbox rows.</summary>
internal sealed class SecondWidgetSyncedHandler(HandlerInvocationRecorder recorder)
    : IMessageHandler<WidgetSyncedEvent>
{
    public ValueTask HandleAsync(WidgetSyncedEvent evt, CancellationToken ct = default)
    {
        recorder.Record(nameof(SecondWidgetSyncedHandler), evt.MessageId.Value.ToString());
        return ValueTask.CompletedTask;
    }
}

internal sealed record SyncWidgetCommand(string Payload) : ICommand<Result<Guid>>;

/// <summary>
/// Seeds the outbox through the real production path: build the row with the singleton
/// OutboxMessageFactory (stamping ambient TenantId/CorrelationId/CausationId from IExecutionContext,
/// ADR-MSG-008) and stage it via IOutboxWriter, so TransactionBehavior flushes it in the same
/// transaction. No store method is asserted on anywhere — this is setup, not verification.
/// </summary>
internal sealed class SyncWidgetHandler(
    OutboxMessageFactory factory,
    IOutboxWriter outboxWriter,
    IExecutionContext executionContext)
    : ICommandHandler<SyncWidgetCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(SyncWidgetCommand command, CancellationToken ct = default)
    {
        var evt = new WidgetSyncedEvent { Payload = command.Payload };

        var message = factory.Create(
            evt,
            evt.MessageId.Value,
            evt.OccurredOnUtc,
            executionContext);

        await outboxWriter.AddAsync(message, ct).ConfigureAwait(false);

        return Success(evt.MessageId.Value);
    }
}
