namespace MicroKit.Messaging.Spi;

/// <summary>
/// Turns a message into bytes of a declared content type, and back. Type-based in both directions.
/// </summary>
/// <remarks>
/// <para>
/// <b>Type-based, not name-based.</b> The serializer is handed the CLR type to read or write; it
/// never resolves a contract name or an assembly-qualified type name. The lookup from a wire type
/// to a local type belongs to the contract registry.
/// </para>
/// <para>
/// The default is System.Text.Json, configured by the host through
/// <c>MessagingBuilder.ConfigureJson</c>; the same options are used to serialize and to
/// deserialize.
/// </para>
/// <para>
/// The outbox and inbox rows store the body as text and assume <c>application/json</c>. A
/// serializer with another content type works on the bus only.
/// </para>
/// </remarks>
public interface IMessageSerializer
{
    /// <summary>Gets the media type <see cref="Serialize"/> produces, e.g. <c>application/json</c>.</summary>
    string ContentType { get; }

    /// <summary>Serializes a message as <paramref name="type"/>.</summary>
    /// <param name="message">The message. Never null.</param>
    /// <param name="type">
    /// The type to serialize as — the message's runtime type, so that a derived type is never
    /// truncated to a declared base.
    /// </param>
    /// <returns>The encoded body.</returns>
    ReadOnlyMemory<byte> Serialize(object message, Type type);

    /// <summary>Deserializes a body into <paramref name="type"/>.</summary>
    /// <param name="body">The encoded body.</param>
    /// <param name="type">The local type to read.</param>
    /// <returns>
    /// The message, or <see langword="null"/> when the body denotes no value. An implementation may
    /// also throw on a malformed body; callers treat both as permanently undeserializable.
    /// </returns>
    object? Deserialize(ReadOnlyMemory<byte> body, Type type);
}
