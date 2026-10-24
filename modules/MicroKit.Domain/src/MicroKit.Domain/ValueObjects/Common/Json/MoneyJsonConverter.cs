using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MicroKit.Domain.ValueObjects.Common.Json;

/// <summary>
/// Serializes a <see cref="Money"/> as <c>{ "Amount": …, "Currency": "EUR" }</c> and reads it back
/// through the <see cref="Money"/> constructor, so an unknown code or an excess scale is rejected.
/// </summary>
/// <remarks>
/// Property names follow <see cref="JsonSerializerOptions.PropertyNamingPolicy"/> and are matched
/// case-insensitively when <see cref="JsonSerializerOptions.PropertyNameCaseInsensitive"/> is set.
/// <see cref="Money.IsZero"/> is derived, so it is not written, and ignored when read.
/// </remarks>
internal sealed class MoneyJsonConverter : JsonConverter<Money>
{
    private const string AmountName = nameof(Money.Amount);
    private const string CurrencyName = nameof(Money.Currency);

    public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Money is serialized as a JSON object with an amount and a currency.");

        var amountName = ConvertName(AmountName, options);
        var currencyName = ConvertName(CurrencyName, options);
        var comparison = options.PropertyNameCaseInsensitive
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        decimal? amount = null;
        string? code = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                if (amount is null || code is null)
                    throw new JsonException($"Money requires both '{amountName}' and '{currencyName}'.");

                return new Money(amount.Value, Currency.FromCode(code));
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Unexpected token in a Money object.");

            var name = reader.GetString();
            reader.Read();

            if (string.Equals(name, amountName, comparison))
                amount = ReadAmount(ref reader, options);
            else if (string.Equals(name, currencyName, comparison))
                code = reader.TokenType == JsonTokenType.String
                    ? reader.GetString()
                    : throw new JsonException($"'{currencyName}' is an ISO 4217 code, a JSON string.");
            else
                reader.Skip();
        }

        throw new JsonException("Unterminated Money object.");
    }

    public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(options);

        writer.WriteStartObject();
        writer.WriteNumber(ConvertName(AmountName, options), value.Amount);
        writer.WriteString(ConvertName(CurrencyName, options), value.Currency.Code);
        writer.WriteEndObject();
    }

    private static decimal ReadAmount(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return reader.GetDecimal();

        if (reader.TokenType == JsonTokenType.String
            && options.NumberHandling.HasFlag(JsonNumberHandling.AllowReadingFromString)
            && decimal.TryParse(reader.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            return parsed;

        throw new JsonException($"'{ConvertName(AmountName, options)}' is a JSON number.");
    }

    private static string ConvertName(string name, JsonSerializerOptions options) =>
        options.PropertyNamingPolicy?.ConvertName(name) ?? name;
}
