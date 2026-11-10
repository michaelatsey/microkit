namespace MicroKit.Messaging.Spi;

/// <summary>
/// The wire form of one message: what a transport hands to a broker, and what a receiving process
/// is given back. Its attributes are aligned on CloudEvents.
/// </summary>
/// <remarks>
/// <para>
/// Part of the <b>transport SPI</b> (<c>MicroKit.Messaging.Spi</c>): a broker provider maps it to
/// and from its native message, and application code never touches it.
/// </para>
/// <para>
/// <b>Attributes.</b> <see cref="Id"/>, <see cref="Source"/>, <see cref="Type"/>,
/// <see cref="Time"/>, <see cref="DataContentType"/> and <see cref="DataSchema"/> are the
/// CloudEvents attributes of the same names. A provider maps them onto the CloudEvents binding of
/// its broker, so a non-.NET consumer reads a standard event.
/// </para>
/// <para>
/// <b>Extensions travel in <see cref="Headers"/></b>: <c>tenantid</c>, <c>correlationid</c>,
/// <c>causationid</c> and <c>traceparent</c> (see <see cref="MessageHeaders"/>), plus any header
/// the producer added. Adding a header never changes this type, which is why the type is a class
/// with init-only members and no positional constructor: a new member is additive for every
/// provider and every call site.
/// </para>
/// <para>
/// <b>The body is opaque bytes</b>, encoded as <see cref="DataContentType"/> says. Nothing on the
/// transport path deserializes it.
/// </para>
/// </remarks>
public sealed class MessageEnvelope
{
    /// <summary>
    /// Gets the message identifier (CloudEvents <c>id</c>). MicroKit producers write a GUID; a
    /// receiving MicroKit process rejects anything else as permanently unprocessable.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>Gets the producing module (CloudEvents <c>source</c>).</summary>
    public required string Source { get; init; }

    /// <summary>
    /// Gets the wire type (CloudEvents <c>type</c>): <c>&lt;name&gt;.v&lt;version&gt;</c>, from
    /// <see cref="MessageContractAttribute.WireType"/>.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>Gets when the fact happened (CloudEvents <c>time</c>).</summary>
    public required DateTimeOffset Time { get; init; }

    /// <summary>
    /// Gets the media type of <see cref="Body"/> (CloudEvents <c>datacontenttype</c>), e.g.
    /// <c>application/json</c>.
    /// </summary>
    public required string DataContentType { get; init; }

    /// <summary>Gets the schema the body adheres to (CloudEvents <c>dataschema</c>), if any.</summary>
    public string? DataSchema { get; init; }

    /// <summary>Gets the extension attributes and producer headers. Never null.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets the serialized message.</summary>
    public required ReadOnlyMemory<byte> Body { get; init; }
}
