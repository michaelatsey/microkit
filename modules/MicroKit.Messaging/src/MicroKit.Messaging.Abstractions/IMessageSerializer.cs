namespace MicroKit.Messaging;

/// <summary>
/// Serializes and deserializes messaging payloads to and from their wire format (JSON by default).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Serialize"/> operates on the runtime type of the payload (<c>payload.GetType()</c>),
/// never on a static generic type parameter, to ensure that concrete subtype properties are
/// preserved when the payload is referenced through an interface.
/// </para>
/// <para>
/// The default implementation (<c>SystemTextJsonMessageSerializer</c> in
/// <c>MicroKit.Messaging</c> Core) uses reflection-based <c>System.Text.Json</c>.
/// A source-generator–based implementation is planned for v2 via
/// <c>MicroKit.Messaging.Serialization</c>.
/// </para>
/// <para>
/// <strong>PROVISIONAL PLACEMENT (pending ADR):</strong> this contract currently lives in
/// <c>MicroKit.Messaging.Abstractions</c>. It may move to a future
/// <c>MicroKit.Messaging.Serialization</c> package once a dependency on
/// <c>System.Text.Json</c> is introduced (which is not permitted in
/// <c>MicroKit.Messaging.Abstractions</c>). A dedicated ADR must be ratified before any
/// such move; until then, the placement must not be changed.
/// </para>
/// </remarks>
public interface IMessageSerializer
{
    /// <summary>
    /// Serializes <paramref name="payload"/> to its JSON wire representation.
    /// Uses <c>payload.GetType()</c> as the serialization type so that properties defined
    /// on the concrete runtime type are included even when the reference is typed as an interface.
    /// Accepts any object: <see cref="IIntegrationEvent"/>, domain event notifications, or
    /// any other serializable payload.
    /// </summary>
    /// <param name="payload">The payload to serialize. Must not be <see langword="null"/>.</param>
    /// <returns>The JSON string representation of <paramref name="payload"/>.</returns>
    string Serialize(object payload);

    /// <summary>
    /// Deserializes a JSON <paramref name="payload"/> back to an instance of the type identified
    /// by <paramref name="eventType"/>. The returned object may be an
    /// <see cref="IIntegrationEvent"/>, a domain-event notification, or any other payload type;
    /// callers re-narrow to the kind they expect.
    /// </summary>
    /// <param name="payload">The JSON string to deserialize.</param>
    /// <param name="eventType">
    /// The assembly-qualified CLR type name of the target type
    /// (e.g. the value stored in <c>OutboxMessage.EventType</c> or
    /// <c>InboxMessage.EventType</c>).
    /// </param>
    /// <returns>
    /// The deserialized payload, or <see langword="null"/> if
    /// <paramref name="eventType"/> cannot be resolved or <paramref name="payload"/>
    /// is malformed. Never throws — callers must handle a <see langword="null"/> return
    /// as a deserialization failure.
    /// </returns>
    object? Deserialize(string payload, string eventType);
}
