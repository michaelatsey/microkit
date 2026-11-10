namespace MicroKit.Messaging;

/// <summary>
/// The message bus: the application's entry point to messaging. Publishes integration events
/// through <see cref="IEventPublisher.PublishAsync{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Code that only publishes should depend on the narrower <see cref="IEventPublisher"/>. Both
/// resolve to the same scoped instance.
/// </para>
/// <para>
/// The bus works end to end with no persistence: a publication goes straight to the transport, and
/// a received message is handled inline by every <see cref="IMessageHandler{T}"/> registered for
/// its contract.
/// </para>
/// </remarks>
public interface IMessageBus : IEventPublisher;
