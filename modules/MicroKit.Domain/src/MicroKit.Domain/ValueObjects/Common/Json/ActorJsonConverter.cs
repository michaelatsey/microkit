using System.Text.Json;
using System.Text.Json.Serialization;
using MicroKit.Domain.Exceptions;

namespace MicroKit.Domain.ValueObjects.Common.Json;

/// <summary>
/// The default <c>System.Text.Json</c> converter of <see cref="Actor"/>: writes the identifier of
/// every actor and reads it back through the public constructor.
/// </summary>
/// <remarks>
/// <para>
/// The converter is asymmetric by design (ADR-004 D1). Writing carries no risk, so it writes every
/// actor, built-in ones included, as its identifier, a JSON string, and as a dictionary key: an HTTP
/// response, a log or a cache written with default options never fails on an actor.
/// </para>
/// <para>
/// Reading is where the risk lies. JSON reaching <see cref="Actor"/> through this type-level
/// converter is untrusted, so a value with the reserved prefix <see cref="Actor.ReservedPrefix"/>
/// fails as a <see cref="JsonException"/> carrying the JSON path, as any other invalid identifier
/// does. A built-in actor written with default options therefore cannot be read back with them:
/// storage the application wrote itself reads it by registering
/// <see cref="TrustedActorJsonConverter"/> in <see cref="JsonSerializerOptions.Converters"/>.
/// </para>
/// </remarks>
internal sealed class ActorJsonConverter : JsonConverter<Actor>
{
    public override Actor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Build(ReadValue(ref reader), static value => new Actor(value));

    public override void Write(Utf8JsonWriter writer, Actor value, JsonSerializerOptions options) =>
        WriteValue(writer, value);

    public override Actor ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Build(reader.GetString()!, static value => new Actor(value));

    public override void WriteAsPropertyName(Utf8JsonWriter writer, Actor value, JsonSerializerOptions options) =>
        WritePropertyName(writer, value);

    /// <summary>Reads the identifier of an actor, a JSON string.</summary>
    internal static string ReadValue(ref Utf8JsonReader reader) =>
        reader.TokenType == JsonTokenType.String
            ? reader.GetString()!
            : throw new JsonException("An actor is serialized as its identifier, a JSON string.");

    /// <summary>Builds an actor, reporting a rejected identifier as a <see cref="JsonException"/>.</summary>
    internal static Actor Build(string value, Func<string, Actor> factory)
    {
        try
        {
            return factory(value);
        }
        catch (DomainException ex)
        {
            throw new JsonException(ex.Message, ex);
        }
    }

    /// <summary>Writes the identifier of an actor.</summary>
    internal static void WriteValue(Utf8JsonWriter writer, Actor value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStringValue(value.Value);
    }

    /// <summary>Writes the identifier of an actor as a property name.</summary>
    internal static void WritePropertyName(Utf8JsonWriter writer, Actor value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        writer.WritePropertyName(value.Value);
    }
}
