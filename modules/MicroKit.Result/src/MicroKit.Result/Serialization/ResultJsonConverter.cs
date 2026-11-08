using System.Text.Json;
using System.Text.Json.Serialization;

namespace MicroKit.Result.Serialization;

/// <summary>
/// System.Text.Json converter for the non-generic <see cref="Result"/>:
/// <c>{"isSuccess":true}</c> or <c>{"isSuccess":false,"error":{…}}</c>.
/// </summary>
/// <remarks>
/// <see cref="Result"/> carries it as its <see cref="JsonConverterAttribute"/>, so a <see cref="Result"/> returned by
/// an endpoint is written this way even when no converter is registered, and never emits <see cref="IError.Metadata"/>
/// by default (ADR-RESULT-001 D1, D12). It uses no reflection. By default it writes the
/// <see cref="ResultJsonView.Client"/> view, which that member documents: what a client may read. Created with
/// <see cref="ResultJsonView.Full"/>, the error object holds <c>code</c>, <c>message</c>, <c>category</c>,
/// <c>severity</c>, <c>propertyName</c> for a validation error, <c>errors</c> for an <see cref="ErrorCollection"/>, and
/// <c>metadata</c>.
/// </remarks>
public sealed class ResultJsonConverter : JsonConverter<Result>
{
    private readonly ResultJsonView _view;

    /// <summary>Initializes a converter that writes the <see cref="ResultJsonView.Client"/> view.</summary>
    public ResultJsonConverter() : this(ResultJsonView.Client)
    {
    }

    /// <summary>Initializes a converter.</summary>
    /// <param name="view">
    /// <see cref="ResultJsonView.Client"/> for anything a client may read; <see cref="ResultJsonView.Full"/> only for
    /// in-process storage, such as a cache, whose payload never reaches a client.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="view"/> is not a defined <see cref="ResultJsonView"/>.</exception>
    public ResultJsonConverter(ResultJsonView view) => _view = ResultGuard.DefinedView(view);

    /// <inheritdoc/>
    public override Result Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected StartObject token.");

        bool? isSuccess = null;
        IError? error = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Expected PropertyName token.");

            var propertyName = reader.GetString();
            reader.Read();
            switch (propertyName)
            {
                case "isSuccess":
                    if (isSuccess is not null)
                        throw new JsonException("Duplicate 'isSuccess' property.");
                    isSuccess = reader.GetBoolean();
                    break;
                case "error":
                    if (error is not null)
                        throw new JsonException("Duplicate 'error' property.");
                    error = ErrorJson.Read(ref reader);
                    break;
                case "value":
                    throw new JsonException("A non-generic result cannot carry a 'value' property.");
                default:
                    reader.Skip();
                    break;
            }
        }

        // A payload that does not describe a valid result fails: it is never read as a success.
        return isSuccess switch
        {
            null => throw new JsonException("Missing 'isSuccess' property."),
            true when error is not null => throw new JsonException("A successful result cannot carry an 'error' property."),
            true => Result.Success(),
            false when error is null => throw new JsonException("A failed result requires an 'error' property."),
            false => Result.Failure(error),
        };
    }

    /// <inheritdoc/>
    /// <exception cref="JsonException">Thrown when <paramref name="value"/> is uninitialized (<c>default</c>).</exception>
    public override void Write(Utf8JsonWriter writer, Result value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (!value.IsSuccess && !value.IsFailure)
            throw new JsonException("Cannot serialize an uninitialized (default) Result: it is neither a success nor a failure.");

        writer.WriteStartObject();
        writer.WriteBoolean("isSuccess", value.IsSuccess);
        if (value.IsFailure)
        {
            writer.WritePropertyName("error");
            ErrorJson.Write(writer, value.Error, _view);
        }
        writer.WriteEndObject();
    }
}
