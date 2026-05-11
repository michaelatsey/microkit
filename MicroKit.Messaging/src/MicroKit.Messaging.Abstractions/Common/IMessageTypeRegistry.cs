namespace MicroKit.Messaging.Abstractions.Common;

/// <summary>Registry for mapping message type names to their CLR envelope types.</summary>
public interface IMessageTypeRegistry
{
    /// <summary>Registers an envelope type under the given message type name.</summary>
    /// <param name="messageType">The fully-qualified name identifying the message type.</param>
    /// <param name="envelopeType">The CLR <see cref="Type"/> of the envelope (e.g. <c>EventEnvelope&lt;T&gt;</c>).</param>
    void Register(string messageType, Type envelopeType);

    /// <summary>Resolves an envelope type by its message type name.</summary>
    /// <param name="messageType">The fully-qualified message type name.</param>
    /// <returns>The registered <see cref="Type"/>, or <c>null</c> if not found.</returns>
    Type? Resolve(string messageType);
}
