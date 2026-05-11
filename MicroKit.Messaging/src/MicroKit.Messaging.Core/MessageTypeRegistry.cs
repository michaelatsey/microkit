using MicroKit.Messaging.Abstractions.Common;
using System.Collections.Concurrent;

namespace MicroKit.Messaging.Core;

/// <summary>Thread-safe in-memory registry mapping message type names to their CLR envelope types.</summary>
public sealed class MessageTypeRegistry : IMessageTypeRegistry
{
    private readonly ConcurrentDictionary<string, Type> _types = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public void Register(string messageType, Type envelopeType)
    {
        ArgumentException.ThrowIfNullOrEmpty(messageType);
        ArgumentNullException.ThrowIfNull(envelopeType);
        _types[messageType] = envelopeType;
    }

    /// <inheritdoc />
    public Type? Resolve(string messageType) =>
        _types.TryGetValue(messageType, out var t) ? t : null;
}
