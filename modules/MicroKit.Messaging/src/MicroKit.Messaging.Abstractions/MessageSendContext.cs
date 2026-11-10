namespace MicroKit.Messaging;

/// <summary>The publication a send middleware observes.</summary>
public sealed class MessageSendContext
{
    /// <summary>Gets the message being published.</summary>
    public required object Message { get; init; }

    /// <summary>Gets the runtime type the message is serialized as.</summary>
    public required Type MessageType { get; init; }

    /// <summary>Gets the identifier assigned to the message.</summary>
    public required MessageId MessageId { get; init; }

    /// <summary>Gets the wire type, <c>&lt;name&gt;.v&lt;version&gt;</c>.</summary>
    public required string WireType { get; init; }

    /// <summary>
    /// Gets the headers the envelope will carry. Mutable; reserved headers set here are
    /// overwritten.
    /// </summary>
    public IDictionary<string, string> Headers { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
