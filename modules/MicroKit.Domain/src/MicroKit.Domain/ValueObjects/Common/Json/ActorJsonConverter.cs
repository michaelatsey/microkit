using System.Text.Json;
using System.Text.Json.Serialization;
using MicroKit.Domain.Exceptions;

namespace MicroKit.Domain.ValueObjects.Common.Json;

/// <summary>
/// The default <c>System.Text.Json</c> converter of <see cref="Actor"/>: writes the identifier and
/// reads it back through the public constructor.
/// </summary>
/// <remarks>
/// <para>
/// JSON reaching <see cref="Actor"/> through this type-level converter is untrusted, so a value with
/// the reserved prefix <see cref="Actor.ReservedPrefix"/> fails as a <see cref="JsonException"/>
/// carrying the JSON path, as any other invalid identifier does.
/// </para>
/// <para>
/// What this converter writes, it can read: it refuses to write a built-in actor too, with a
/// <see cref="JsonException"/> naming <see cref="TrustedActorJsonConverter"/>. A value that cannot
/// cross the untrusted path therefore fails when it is written, inside the business transaction,
/// never later when it is read back. Storage the application wrote itself opts into built-in actors
/// by registering <see cref="TrustedActorJsonConverter"/>. An actor also works as a dictionary key,
/// under the same rule.
/// </para>
/// </remarks>
internal sealed class ActorJsonConverter : JsonConverter<Actor>
{
    public override Actor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Build(ReadValue(ref reader), static value => new Actor(value));

    public override void Write(Utf8JsonWriter writer, Actor value, JsonSerializerOptions options) =>
        WriteValue(writer, RequireUntrustedSafe(value));

    public override Actor ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Build(reader.GetString()!, static value => new Actor(value));

    public override void WriteAsPropertyName(Utf8JsonWriter writer, Actor value, JsonSerializerOptions options) =>
        WritePropertyName(writer, RequireUntrustedSafe(value));

    /// <summary>
    /// Refuses a built-in actor, which this converter could not read back.
    /// </summary>
    private static Actor RequireUntrustedSafe(Actor value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Value.StartsWith(Actor.ReservedPrefix))
            throw new JsonException(
                $"The built-in actor '{value.Value}' cannot be serialized with the default Actor converter, " +
                $"which could not read it back. Register {nameof(TrustedActorJsonConverter)} in " +
                "JsonSerializerOptions.Converters for storage the application wrote itself.");

        return value;
    }

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
