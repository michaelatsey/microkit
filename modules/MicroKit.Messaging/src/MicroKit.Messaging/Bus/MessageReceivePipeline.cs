namespace MicroKit.Messaging.Bus;

using MicroKit.Messaging.Execution;
using MicroKit.Messaging.Publishing;
using MicroKit.Messaging.Registry;
using MessageCtx = MicroKit.Messaging.Execution.ExecutionContext;

/// <summary>
/// The receive pipeline: from one envelope to every handler of its contract, inline, with a
/// verdict for the transport.
/// </summary>
/// <remarks>
/// <para>
/// <b>Stages.</b> The contract, the identifier and the body are resolved once, before any scope:
/// an unknown contract, an identifier that is not a GUID and an undeserializable body are
/// permanent, so such a message is dead-lettered on its first delivery and reaches no handler.
/// Then, for each handler of the contract, in its own scope: the scope is created through
/// <see cref="IExecutionScopeFactory"/> and its execution context filled from the envelope —
/// tenant from <c>tenantid</c>, correlation from <c>correlationid</c>, causation set to the
/// incoming message id — then the <see cref="IMessageScopeInitializer"/> instances run in
/// registration order, then the <see cref="IMessageReceiveMiddleware"/> chain, then the handler,
/// resolved last.
/// </para>
/// <para>
/// <b>Verdicts.</b> <see cref="PermanentMessageException"/> and <see cref="InboxPayloadException"/>
/// are permanent; <see cref="InboxDependencyUnavailableException"/> is dependency-unavailable; any
/// other exception is transient. Several handlers yield the most redeliverable of their verdicts.
/// </para>
/// <para>
/// <b>Shutdown is not a failure.</b> An <see cref="OperationCanceledException"/> while the host
/// token is cancelled, or an <see cref="ObjectDisposedException"/> from a disposed scope or
/// provider (or raised while the host token is cancelled), stops the delivery at once with <see cref="DeliveryVerdict.Unsettled"/>: never
/// classified, never counted.
/// </para>
/// <para>
/// <b>At-least-once, and handlers may rerun.</b> With no inbox, a message redelivered because one
/// handler failed runs every handler again, including those that had already succeeded. #188 adds
/// the per-handler guard.
/// </para>
/// </remarks>
internal sealed class MessageReceivePipeline(
    IntegrationEventRegistry contracts,
    MessageHandlerRegistry handlers,
    IMessageSerializer serializer,
    IExecutionScopeFactory scopeFactory,
    ILogger<MessageReceivePipeline> logger)
{
    /// <summary>Delivers one envelope to every handler of its contract.</summary>
    /// <param name="envelope">The envelope.</param>
    /// <param name="ct">The host's token; its cancellation means shutdown.</param>
    /// <returns>What the transport must do with the message.</returns>
    public async ValueTask<DeliveryOutcome> DeliverAsync(
        MessageEnvelope envelope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!contracts.TryResolveLocalType(envelope.Type, out var eventType))
        {
            return DeliveryOutcome.Permanent(
                $"Wire type '{envelope.Type}' resolves to no local type in this process. Register " +
                "a type carrying the matching [MessageContract].");
        }

        if (!Guid.TryParse(envelope.Id, out var id))
        {
            return DeliveryOutcome.Permanent($"Message id '{envelope.Id}' is not a GUID.");
        }

        IIntegrationEvent message;
        try
        {
            message = serializer.Deserialize(envelope.Body, eventType) as IIntegrationEvent
                ?? throw new InboxPayloadException(
                    $"The body deserialized to null or to a type that is not '{eventType.FullName}'.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return DeliveryOutcome.Permanent(
                $"The body of '{envelope.Type}' cannot be deserialized into '{eventType.FullName}'.",
                ex);
        }

        var entries = handlers.GetHandlers(eventType);
        if (entries.Count == 0)
        {
            BusLogs.NoHandler(logger, envelope.Id, envelope.Type, envelope.Source, eventType.FullName!);
            return DeliveryOutcome.Success;
        }

        var context = BuildContext(envelope, eventType, MessageId.From(id));
        var outcome = DeliveryOutcome.Success;

        foreach (var entry in entries)
        {
            var handlerOutcome = await DeliverToHandlerAsync(entry, message, context, envelope, ct)
                .ConfigureAwait(false);

            if (handlerOutcome.Verdict is DeliveryVerdict.Unsettled)
            {
                return handlerOutcome;
            }

            outcome = DeliveryOutcome.MostRedeliverable(outcome, handlerOutcome);
        }

        return outcome;
    }

    private async ValueTask<DeliveryOutcome> DeliverToHandlerAsync(
        MessageHandlerRegistry.HandlerEntry entry,
        IIntegrationEvent message,
        MessageContext context,
        MessageEnvelope envelope,
        CancellationToken ct)
    {
        try
        {
            var executionContext = new MessageCtx
            {
                TenantId = context.TenantId,
                CorrelationId = context.CorrelationId,
                CausationId = context.MessageId.ToString(),
            };

            var scope = await scopeFactory.CreateScopeAsync(executionContext, ct).ConfigureAwait(false);
            await using (scope.ConfigureAwait(false))
            {
                var services = scope.ServiceProvider;

                // The single ambient source (decision 1). A scope factory a host substitutes may
                // not fill the holder itself; the pipeline does, before any initializer runs.
                if (services.GetService<ExecutionContextHolder>() is { } holder)
                {
                    holder.Context = executionContext;
                }

                foreach (var initializer in services.GetServices<IMessageScopeInitializer>())
                {
                    await initializer.InitializeAsync(context, services, ct).ConfigureAwait(false);
                }

                var middlewares = services.GetServices<IMessageReceiveMiddleware>().ToArray();
                var receiveContext = new MessageReceiveContext
                {
                    Context = context,
                    Message = message,
                    HandlerType = entry.HandlerType,
                    Services = services,
                };

                await RunAsync(0).ConfigureAwait(false);

                ValueTask RunAsync(int index) =>
                    index < middlewares.Length
                        ? middlewares[index].InvokeAsync(receiveContext, () => RunAsync(index + 1), ct)
                        : InvokeHandlerAsync();

                ValueTask InvokeHandlerAsync()
                {
                    var handler = services.GetService(entry.HandlerType)
                        ?? throw new InvalidOperationException(
                            $"Handler '{entry.HandlerType.FullName}' is in the handler registry but " +
                            "not in the service provider.");

                    return entry.Invoker(handler, message, context, ct);
                }
            }

            return DeliveryOutcome.Success;
        }
        catch (Exception ex) when (IsShutdown(ex, ct))
        {
            BusLogs.Unsettled(logger, ex, envelope.Id, envelope.Type, entry.HandlerType.FullName!);
            return new DeliveryOutcome(DeliveryVerdict.Unsettled, ex);
        }
        catch (Exception ex)
        {
            var verdict = Classify(ex);
            BusLogs.HandlerFailed(
                logger, ex, entry.HandlerType.FullName!, envelope.Id, envelope.Type, verdict);
            return new DeliveryOutcome(verdict, ex, ex.Message);
        }
    }

    /// <summary>
    /// Whether <paramref name="exception"/> is the host shutting down rather than a failure.
    /// </summary>
    /// <remarks>
    /// An <see cref="ObjectDisposedException"/> is shutdown when the host token is cancelled, or
    /// when it names the service provider — the scope or container disposed under the delivery.
    /// One raised by a handler's own object (a disposed <c>HttpClient</c>, a context used after
    /// dispose) while the host keeps running is an ordinary failure: classifying it as shutdown
    /// would leave the message unsettled and stop the consume loop of a live host.
    /// </remarks>
    private static bool IsShutdown(Exception exception, CancellationToken ct) => exception switch
    {
        OperationCanceledException => ct.IsCancellationRequested,
        ObjectDisposedException disposed => ct.IsCancellationRequested
            || string.Equals(disposed.ObjectName, nameof(IServiceProvider), StringComparison.Ordinal),
        _ => false,
    };

    private static DeliveryVerdict Classify(Exception exception) => exception switch
    {
        PermanentMessageException or InboxPayloadException => DeliveryVerdict.Permanent,
        InboxDependencyUnavailableException => DeliveryVerdict.DependencyUnavailable,
        _ => DeliveryVerdict.Transient,
    };

    private static MessageContext BuildContext(
        MessageEnvelope envelope, Type eventType, MessageId messageId)
    {
        // Every type the registry resolves carries the attribute: both builders reject its absence.
        var contract = MessageContracts.Find(eventType);
        var headers = envelope.Headers;

        return new MessageContext
        {
            MessageId = messageId,
            ContractName = contract?.Name ?? envelope.Type,
            ContractVersion = contract?.Version ?? 0,
            Source = envelope.Source,
            OccurredOnUtc = envelope.Time,
            TenantId = headers.GetValueOrDefault(MessageHeaders.TenantId),
            // Minted ONCE when absent, so every handler of this delivery — and every message they
            // publish — shares one chain.
            CorrelationId = headers.GetValueOrDefault(MessageHeaders.CorrelationId)
                ?? Guid.NewGuid().ToString(),
            CausationId = headers.GetValueOrDefault(MessageHeaders.CausationId),
            Headers = headers,
        };
    }
}
