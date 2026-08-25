namespace MicroKit.Messaging.Processing;

using MicroKit.Messaging.Registry;

/// <summary>
/// Fails startup when message handlers are registered but nothing in this release can produce the
/// inbox rows that would reach them.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it guards.</b> In-process inbox ingestion was withdrawn with the in-process fan-out
/// (ADR-MSG-019). <c>MicroKit.Messaging</c> no longer writes an <see cref="InboxMessage"/> anywhere:
/// the outbox hands a <see cref="MessageKind.Contract"/> row to an <see cref="IMessageTransport"/>
/// as a <see cref="MessageEnvelope"/>, and the receiving seam that turns an envelope back into
/// per-consumer inbox rows has not shipped. A host that called
/// <c>MessagingBuilder.AddMessageHandler&lt;THandler, TEvent&gt;()</c> has declared an expectation
/// nothing can meet.
/// </para>
/// <para>
/// <b>Why it throws rather than warns.</b> The shortfall is undetectable from the outside. No row is
/// written, so <c>InboxProcessor</c> claims nothing, logs nothing above <c>Debug</c>, and reports a
/// healthy empty queue — indistinguishable from a system that is simply idle. That is the silent
/// failure this module treats as blocking, and the only honest signal is at boot, before anything
/// depends on a delivery that will not happen.
/// </para>
/// <para>
/// <b>What is <i>not</i> broken.</b> The registry, <c>InboxProcessor</c>, the claim, the settlement
/// and both retention workers are unchanged and still correct. They have no producer, which is not
/// the same as being defective — a host that writes inbox rows itself can drive the whole drain, and
/// the integration suite does exactly that. This validator is deleted when the receiving seam
/// arrives.
/// </para>
/// <para>
/// It resolves in <see cref="StartAsync"/> rather than taking the registry by constructor, for the
/// reason <c>IntegrationEventRegistryValidator</c> gives: the check must be an observable action at
/// a known point in the lifecycle, not a side effect of this type happening to be activated. That
/// also makes it directly testable without standing up a host — which would start four messaging
/// workers needing stores a test has no reason to wire.
/// </para>
/// </remarks>
internal sealed class InboxIngestionValidator(IServiceProvider serviceProvider) : IHostedService
{
    /// <inheritdoc />
    /// <exception cref="InboxConfigurationException">
    /// One or more message handlers are registered while no producer of inbox rows exists.
    /// </exception>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var registry = serviceProvider.GetRequiredService<MessageHandlerRegistry>();
        var consumers = registry.RegisteredConsumerTypes;

        if (consumers.Count == 0)
            return Task.CompletedTask;

        throw new InboxConfigurationException(
            $"{consumers.Count} message handler(s) are registered, but nothing in this release " +
            "writes the inbox rows that would reach them. In-process inbox ingestion was withdrawn " +
            "with the in-process fan-out (ADR-MSG-019): a Contract outbox row is now handed to an " +
            "IMessageTransport as a MessageEnvelope, and the receiving seam that turns an envelope " +
            "back into per-consumer inbox rows has not shipped yet. Remove the " +
            "AddMessageHandler<THandler, TEvent>() call(s) until it does, or write inbox rows " +
            "yourself if you are driving the drain directly. Registered consumers: " +
            $"{string.Join(", ", consumers)}.");
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
