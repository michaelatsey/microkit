namespace MicroKit.Messaging.Spi;

/// <summary>
/// Supplies local consumers: what a local message (<see cref="ILocalMessageSender"/>) is delivered
/// to. Registered with <c>MessagingIntegrationExtensions.AddLocalConsumerSource</c>, on the <c>MessagingBuilder</c>.
/// </summary>
/// <remarks>
/// <para>
/// Read once, when the bus first needs its consumers — after the container is built, so a source
/// may describe registrations made after it was added. A source that cannot turn a registration
/// into a consumer reports it through a start check (<c>MessagingBuilder.AddStartupCheck</c>) rather
/// than leaving it out silently.
/// </para>
/// <para>
/// Each consumer is guarded on its own, with its own inbox row, like an
/// <see cref="IMessageHandler{TMessage}"/>: <see cref="LocalConsumer.ConsumerType"/> is the inbox
/// key, so it must stay stable across deployments.
/// </para>
/// </remarks>
public interface ILocalConsumerSource
{
    /// <summary>The consumers this source supplies.</summary>
    /// <returns>Each consumer once.</returns>
    IEnumerable<LocalConsumer> GetConsumers();
}
