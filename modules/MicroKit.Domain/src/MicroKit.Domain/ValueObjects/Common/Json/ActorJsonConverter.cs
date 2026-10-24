using System.Text.Json;
using System.Text.Json.Serialization;
using MicroKit.Domain.Exceptions;

namespace MicroKit.Domain.ValueObjects.Common.Json;

/// <summary>
/// Serializes an <see cref="Actor"/> as its identifier and reads it back through
/// <see cref="Actor.Restore(string)"/>, so a stored built-in actor round-trips.
/// </summary>
/// <remarks>
/// Because it goes through <see cref="Actor.Restore(string)"/>, reading can produce a built-in
/// actor. It is meant for JSON the application stored itself, never for a request body. An invalid
/// identifier fails with a <see cref="JsonException"/> whose inner exception is the
/// <see cref="DomainException"/>. An actor also works as a dictionary key.
/// </remarks>
internal sealed class ActorJsonConverter : JsonConverter<Actor>
{
    public override Actor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("An actor is serialized as its identifier, a JSON string.");

        return Restore(reader.GetString()!);
    }

    public override void Write(Utf8JsonWriter writer, Actor value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStringValue(value.Value);
    }

    public override Actor ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Restore(reader.GetString()!);

    public override void WriteAsPropertyName(Utf8JsonWriter writer, Actor value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        writer.WritePropertyName(value.Value);
    }

    private static Actor Restore(string value)
    {
        try
        {
            return Actor.Restore(value);
        }
        catch (DomainException ex)
        {
            throw new JsonException(ex.Message, ex);
        }
    }
}
