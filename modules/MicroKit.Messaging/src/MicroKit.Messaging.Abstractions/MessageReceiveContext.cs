namespace MicroKit.Messaging;

/// <summary>The delivery a receive middleware observes.</summary>
public sealed class MessageReceiveContext
{
    /// <summary>Gets the message's context.</summary>
    public required MessageContext Context { get; init; }

    /// <summary>Gets the deserialized message.</summary>
    public required object Message { get; init; }

    /// <summary>Gets the handler type this delivery is for.</summary>
    public required Type HandlerType { get; init; }

    /// <summary>Gets the service provider of this handler's message scope.</summary>
    public required IServiceProvider Services { get; init; }
}
