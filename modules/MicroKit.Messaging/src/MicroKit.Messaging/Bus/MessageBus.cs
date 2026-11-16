namespace MicroKit.Messaging.Bus;

using System.Diagnostics;
using MicroKit.Messaging.Outbox;
using MicroKit.Messaging.Publishing;
using MicroKit.Messaging.Stores;

/// <summary>
/// The standard <see cref="IMessageBus"/>: with no store configured, publishes straight to the
/// registered transport; with stores, enlists the publication in a store's outbox.
/// </summary>
/// <remarks>
/// <para>
/// <b>Outbox by enlistment.</b> With a store configured, the publication becomes an outbox row
/// added to the store's unit of work in this scope — for EF Core, its <c>DbContext</c> — and nothing
/// is saved here. Whatever saves that unit of work commits the row with the business data it
/// announces; inside a delivery, the engine's save does. The store is chosen by
/// <see cref="MessageStoreResolver"/>: <see cref="PublishOptions.Store"/>, the delivery's store, the
/// only store, or the store bound to the message type's assembly.
/// </para>
/// <para>
/// <b>Scoped, and the scope is the ambient context.</b> Tenant, correlation and causation are read
/// from the scope's <see cref="IExecutionContext"/>, which the receive pipeline fills from the
/// envelope before anything in a message scope is resolved. A handler's publication therefore
/// inherits its message's tenant and correlation and names that message as its causation, with no
/// <c>AsyncLocal</c> anywhere.
/// </para>
/// <para>
/// <b>Every check runs before anything is sent</b>: the contract, the reserved headers, the tenant
/// and the transport. A publication either reaches the transport — or its store's outbox — or throws — including when a send
/// middleware stops the pipeline, which would otherwise return an identifier for a message that
/// never left.
/// </para>
/// </remarks>
internal sealed class MessageBus(
    IntegrationEventRegistry registry,
    IMessageSerializer serializer,
    IExecutionContext executionContext,
    MessageStoreResolver stores,
    DeliveryStoreHolder deliveryStore,
    OutboxMessageFactory outboxFactory,
    IServiceProvider services,
    TimeProvider timeProvider,
    ILogger<MessageBus> logger)
    : IMessageBus
{
    /// <inheritdoc />
    public async ValueTask<MessageId> PublishAsync<T>(
        T message,
        PublishOptions? options = null,
        CancellationToken ct = default)
        where T : class, IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(message);

        var messageType = message.GetType();
        var registration = registry.ResolveContract(messageType);
        RejectReservedHeaders(options?.Headers);
        var tenantId = ResolveTenant(messageType, options?.TenantId);
        var store = stores.ForPublication(messageType, options?.Store, deliveryStore.Store);
        var transport = services.GetService<IMessageTransport>()
            ?? throw new IntegrationEventConfigurationException(
                $"'{messageType.Name}' was published, but no transport is registered, so the " +
                "message has nowhere to go. Call UseInMemoryTransport() on the MessagingBuilder, " +
                "or a broker provider's Add{Provider}Transport().");

        var messageId = MessageId.New();
        var context = new MessageSendContext
        {
            Message = message,
            MessageType = messageType,
            MessageId = messageId,
            WireType = registration.ContractName,
        };

        if (options?.Headers is { } extra)
        {
            foreach (var (name, value) in extra)
            {
                context.Headers[name] = value;
            }
        }

        var middlewares = services.GetServices<IMessageSendMiddleware>().ToArray();
        var sent = false;

        await RunAsync(0).ConfigureAwait(false);

        if (!sent)
        {
            throw new IntegrationEventPublishException(
                $"'{messageType.Name}' (message {messageId}) was not sent: a send middleware " +
                "completed without calling the rest of the pipeline. A publication either reaches " +
                "the transport or fails; it never succeeds silently.");
        }

        if (store is null)
        {
            BusLogs.Published(
                logger, messageId.ToString(), registration.ContractName, registry.Source, tenantId);
        }
        else
        {
            BusLogs.Enlisted(
                logger, messageId.ToString(), registration.ContractName, store.Value, tenantId);
        }

        return messageId;

        ValueTask RunAsync(int index) =>
            index < middlewares.Length
                ? middlewares[index].InvokeAsync(context, () => RunAsync(index + 1), ct)
                : SendAsync();

        async ValueTask SendAsync()
        {
            var headers = BuildHeaders(context.Headers, tenantId);
            var time = timeProvider.GetUtcNow();

            if (store is not null)
            {
                var row = outboxFactory.CreateContract(
                    message, messageId, registration.ContractName, registry.Source, time, headers);
                var writer = services.GetRequiredKeyedService<IOutboxWriter>(store);
                await writer.AddAsync(row, ct).ConfigureAwait(false);
                sent = true;
                return;
            }

            var envelope = new MessageEnvelope
            {
                Id = messageId.ToString(),
                Source = registry.Source,
                Type = registration.ContractName,
                Time = time,
                DataContentType = serializer.ContentType,
                Headers = headers,
                Body = serializer.Serialize(message, messageType),
            };

            await transport.SendAsync(envelope, ct).ConfigureAwait(false);
            sent = true;
        }
    }

    /// <summary>
    /// Writes the reserved headers after the send middleware ran, overwriting — or removing —
    /// anything it set: a middleware cannot forge the tenant, the chain or the trace.
    /// </summary>
    private Dictionary<string, string> BuildHeaders(
        IDictionary<string, string> middlewareHeaders, string? tenantId)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (name, value) in middlewareHeaders)
        {
            if (!MessageHeaders.IsReserved(name))
            {
                headers[name] = value;
            }
        }

        if (tenantId is not null)
        {
            headers[MessageHeaders.TenantId] = tenantId;
        }

        headers[MessageHeaders.CorrelationId] =
            executionContext.CorrelationId ?? Guid.NewGuid().ToString();

        if (executionContext.CausationId is { } causationId)
        {
            headers[MessageHeaders.CausationId] = causationId;
        }

        if (Activity.Current?.Id is { } traceParent)
        {
            headers[MessageHeaders.TraceParent] = traceParent;
        }

        return headers;
    }

    private string? ResolveTenant(Type messageType, string? explicitTenant)
    {
        var ambientTenant = executionContext.TenantId;

        if (explicitTenant is not null
            && ambientTenant is not null
            && !string.Equals(explicitTenant, ambientTenant, StringComparison.Ordinal))
        {
            throw new IntegrationEventPublishException(
                $"'{messageType.Name}' was published for tenant '{explicitTenant}' from a scope " +
                $"whose tenant is '{ambientTenant}'. An explicit tenant may only name the ambient " +
                "one, or supply one where there is none; it never re-attributes a message to " +
                "another tenant. Nothing was sent.");
        }

        return explicitTenant ?? ambientTenant;
    }

    private static void RejectReservedHeaders(IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null)
        {
            return;
        }

        foreach (var name in headers.Keys)
        {
            if (MessageHeaders.IsReserved(name))
            {
                throw new ArgumentException(
                    $"Header '{name}' is reserved and set by the bus. Supply a tenant through " +
                    "PublishOptions.TenantId; correlation, causation and trace come from the " +
                    "ambient context.",
                    nameof(headers));
            }
        }
    }
}
