using MicroKit.Execution.Abstractions;
using MicroKit.Persistence.Abstractions;

namespace MicroKit.Messaging.MediatR.IntegrationTests.Fixtures;

/// <summary>
/// The consuming half of the round trip: two handlers for the one contract
/// <c>ReentrantOutboxFixtures</c> publishes, so the receiving seam's fan-out has more than one
/// consumer to fan out to.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two, not one, and the second is not padding.</b> Three of the five properties the retired
/// <c>InboxRedeliveryTests</c> carried are invisible with a single consumer: one row per consumer,
/// a duplicate absorbed for one consumer not costing the next its row, and the sibling isolation
/// of the compound dedup key. A one-consumer test passes under an implementation that returns on
/// the first duplicate — which is the defect ADR-MSG-017 §6 exists to prevent.
/// </para>
/// <para>
/// <b>Each handler writes and commits.</b> That is not incidental to the scenario: the inbox
/// stages its processed mark into the handler's own unit of work, so a handler that commits nothing
/// exercises the deferred fallback rather than the transactional guarantee. Writing through the
/// scope's own <c>DbContext</c> — reached via <c>IE2EWriter</c> and <c>IUnitOfWork</c>, never by
/// injecting the context — is what makes the mark and the side effect commit together.
/// </para>
/// </remarks>
internal abstract class RecordingWidgetShippedHandler(
    IExecutionContext executionContext,
    IE2EWriter writer,
    IUnitOfWork unitOfWork,
    HandlerInvocationRecorder recorder)
    : IMessageHandler<WidgetShipped>
{
    public async ValueTask HandleAsync(WidgetShipped evt, CancellationToken ct = default)
    {
        // The causation the PROCESSOR derived for this scope, not anything off the event. That is
        // the whole subject of the end-to-end assertion: work done while handling a delivery must
        // name the delivery as its cause, and the only value that answers "which message caused
        // this" in another process is the inbox row's MessageId.
        recorder.Record(GetType().Name, executionContext.CausationId ?? "<null>");

        writer.Stage(new E2EAggregate { Name = $"handled-{evt.WidgetId}" });

        // Commits the handler's own write AND the staged processed mark, together or not at all.
        await unitOfWork.CommitAsync(ct);
    }
}

internal sealed class FirstWidgetShippedConsumer(
    IExecutionContext executionContext,
    IE2EWriter writer,
    IUnitOfWork unitOfWork,
    HandlerInvocationRecorder recorder)
    : RecordingWidgetShippedHandler(executionContext, writer, unitOfWork, recorder);

internal sealed class SecondWidgetShippedConsumer(
    IExecutionContext executionContext,
    IE2EWriter writer,
    IUnitOfWork unitOfWork,
    HandlerInvocationRecorder recorder)
    : RecordingWidgetShippedHandler(executionContext, writer, unitOfWork, recorder);
