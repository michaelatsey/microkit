using System.Text.Json;
using System.Text.Json.Serialization;
using MicroKit.Domain.Exceptions;

namespace MicroKit.Domain.ValueObjects.Common.Json;

/// <summary>
/// Serializes a <see cref="Currency"/> as its ISO 4217 code and reads it back through
/// <see cref="Currency.FromCode(string)"/>, so the minor unit always comes from the table.
/// </summary>
/// <remarks>
/// A currency also works as a dictionary key. An unknown code fails with a
/// <see cref="JsonException"/> whose inner exception is the <see cref="DomainException"/>.
/// </remarks>
internal sealed class CurrencyJsonConverter : JsonConverter<Currency>
{
    public override Currency Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("A currency is serialized as its ISO 4217 code, a JSON string.");

        return FromCode(reader.GetString()!);
    }

    public override void Write(Utf8JsonWriter writer, Currency value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(CodeOf(value));
    }

    public override Currency ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        FromCode(reader.GetString()!);

    public override void WriteAsPropertyName(Utf8JsonWriter writer, Currency value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WritePropertyName(CodeOf(value));
    }

    /// <summary>Reads a currency code, reporting an unknown one as a <see cref="JsonException"/>.</summary>
    internal static Currency FromCode(string code)
    {
        try
        {
            return Currency.FromCode(code);
        }
        catch (DomainException ex)
        {
            throw new JsonException(ex.Message, ex);
        }
    }

    /// <summary>Returns the code to write, refusing <c>default(Currency)</c>, which is not a currency.</summary>
    internal static string CodeOf(Currency value) =>
        value.Code ?? throw new JsonException("default(Currency) is not a currency and cannot be serialized.");
}
