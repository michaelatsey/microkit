namespace MicroKit.Messaging;

/// <summary>
/// Serializes and deserializes <see cref="IIntegrationEvent"/> instances to and from
/// their wire format (JSON by default).
/// </summary>
/// <remarks>
/// <para>
/// Both methods operate on the runtime type of the event (<c>evt.GetType()</c>), never
/// on a static generic type parameter, to ensure that concrete subtype properties are
/// preserved when an event is referenced through an interface.
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
    /// Serializes <paramref name="evt"/> to its JSON wire representation.
    /// Uses <c>evt.GetType()</c> as the serialization type so that properties defined
    /// on the concrete subtype are included even when the reference is typed as
    /// <see cref="IIntegrationEvent"/>.
    /// </summary>
    /// <param name="evt">The integration event to serialize. Must not be <see langword="null"/>.</param>
    /// <returns>The JSON string representation of <paramref name="evt"/>.</returns>
    string Serialize(IIntegrationEvent evt);

    /// <summary>
    /// Deserializes a JSON <paramref name="payload"/> back to an
    /// <see cref="IIntegrationEvent"/> instance of the type identified by
    /// <paramref name="eventType"/>.
    /// </summary>
    /// <param name="payload">The JSON string to deserialize.</param>
    /// <param name="eventType">
    /// The assembly-qualified CLR type name of the target event type
    /// (e.g. the value stored in <c>OutboxMessage.EventType</c> or
    /// <c>InboxMessage.EventType</c>).
    /// </param>
    /// <returns>
    /// The deserialized <see cref="IIntegrationEvent"/>, or <see langword="null"/> if
    /// <paramref name="eventType"/> cannot be resolved or <paramref name="payload"/>
    /// is malformed. Never throws — callers must handle a <see langword="null"/> return
    /// as a deserialization failure.
    /// </returns>
    IIntegrationEvent? Deserialize(string payload, string eventType);
}
