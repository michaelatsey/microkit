namespace MicroKit.Messaging.Spi;

/// <summary>One local consumer of one message type.</summary>
/// <param name="MessageType">The message type it consumes, matched exactly.</param>
/// <param name="ConsumerType">Its stable name: with the message id, the key of its inbox row.</param>
/// <param name="HandlerType">The type that handles the message, for logs and for the store binding
/// of its assembly.</param>
/// <param name="Invoke">Runs it, from the message scope's services, on the message and its context.</param>
/// <exception cref="ArgumentNullException">A type or the invoker is <see langword="null"/>.</exception>
/// <exception cref="ArgumentException"><paramref name="ConsumerType"/> is empty.</exception>
public sealed record LocalConsumer(
    Type MessageType,
    string ConsumerType,
    Type HandlerType,
    Func<IServiceProvider, object, MessageContext, CancellationToken, ValueTask> Invoke)
{
    /// <summary>Gets the message type it consumes, matched exactly.</summary>
    public Type MessageType { get; } = MessageType ?? throw new ArgumentNullException(nameof(MessageType));

    /// <summary>Gets its stable name: never empty, since it is the key of its inbox row.</summary>
    public string ConsumerType { get; } = string.IsNullOrEmpty(ConsumerType)
        ? throw new ArgumentException("A local consumer's name is the key of its inbox row: it cannot be empty.", nameof(ConsumerType))
        : ConsumerType;

    /// <summary>Gets the type that handles the message.</summary>
    public Type HandlerType { get; } = HandlerType ?? throw new ArgumentNullException(nameof(HandlerType));

    /// <summary>Gets what runs it.</summary>
    public Func<IServiceProvider, object, MessageContext, CancellationToken, ValueTask> Invoke { get; } =
        Invoke ?? throw new ArgumentNullException(nameof(Invoke));
}
