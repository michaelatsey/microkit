using System.Text.Json;
using System.Text.Json.Serialization;

namespace MicroKit.Domain.ValueObjects.Common.Json;

/// <summary>
/// Serializes a <see cref="Currency"/> as its ISO 4217 code and reads it back through
/// <see cref="Currency.FromCode(string)"/>, so the minor unit always comes from the table.
/// </summary>
internal sealed class CurrencyJsonConverter : JsonConverter<Currency>
{
    public override Currency Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("A currency is serialized as its ISO 4217 code, a JSON string.");

        return Currency.FromCode(reader.GetString()!);
    }

    public override void Write(Utf8JsonWriter writer, Currency value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.Code);
    }
}
