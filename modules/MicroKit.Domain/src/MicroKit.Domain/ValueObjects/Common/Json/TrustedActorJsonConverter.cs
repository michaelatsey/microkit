using System.Text.Json;
using System.Text.Json.Serialization;
using MicroKit.Domain.Exceptions;

namespace MicroKit.Domain.ValueObjects.Common.Json;

/// <summary>
/// A <c>System.Text.Json</c> converter of <see cref="Actor"/> that reads through
/// <see cref="Actor.Restore(string)"/>, so a stored built-in actor round-trips to the same instance.
/// </summary>
/// <remarks>
/// <para>
/// Register it only in <see cref="JsonSerializerOptions.Converters"/>, and only on options that read
/// storage the application wrote itself: an outbox, an event store, a cache. A converter in that
/// collection takes precedence over the type-level <see cref="JsonConverterAttribute"/> of
/// <see cref="Actor"/>, which treats JSON as untrusted and rejects the reserved prefix.
/// </para>
/// <para>
/// Never register it on options that read a request body or any other external input: a payload
/// naming <c>$system</c> would then be read as <see cref="Actor.System"/>, and the audit trail
/// would record a caller's action as the application's.
/// </para>
/// <para>
/// It writes every actor, built-in ones included, as its identifier, a JSON string; the default
/// converter refuses to write a built-in actor. An invalid identifier read back fails as a
/// <see cref="JsonException"/> whose inner exception is the <see cref="DomainException"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var storage = new JsonSerializerOptions { Converters = { new TrustedActorJsonConverter() } };
/// </code>
/// </example>
public sealed class TrustedActorJsonConverter : JsonConverter<Actor>
{
    /// <inheritdoc />
    public override Actor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        ActorJsonConverter.Build(ActorJsonConverter.ReadValue(ref reader), Actor.Restore);

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Actor value, JsonSerializerOptions options) =>
        ActorJsonConverter.WriteValue(writer, value);

    /// <inheritdoc />
    public override Actor ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        ActorJsonConverter.Build(reader.GetString()!, Actor.Restore);

    /// <inheritdoc />
    public override void WriteAsPropertyName(Utf8JsonWriter writer, Actor value, JsonSerializerOptions options) =>
        ActorJsonConverter.WritePropertyName(writer, value);
}
