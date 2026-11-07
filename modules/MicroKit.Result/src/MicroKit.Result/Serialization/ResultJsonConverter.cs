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
/// by default (ADR-RESULT-001 D1, D12). It uses no reflection. Unless created with <c>includeMetadata</c>, it writes
/// <c>"The server could not complete the request."</c> as the message of a fault of the system, whose message may
/// describe it. The error object holds <c>code</c>, <c>message</c>,
/// <c>category</c>, <c>severity</c>, <c>propertyName</c> for a validation error, and <c>errors</c> for an
/// <see cref="ErrorCollection"/>; <c>metadata</c> only when created with <c>includeMetadata</c>.
/// </remarks>
public sealed class ResultJsonConverter : JsonConverter<Result>
{
    private readonly bool _includeMetadata;

    /// <summary>Initializes a converter that never writes <see cref="IError.Metadata"/>.</summary>
    public ResultJsonConverter() : this(includeMetadata: false)
    {
    }

    /// <summary>Initializes a converter.</summary>
    /// <param name="includeMetadata">
    /// <see langword="true"/> to write <see cref="IError.Metadata"/> and the message of a fault of the system, for
    /// in-process storage such as a cache; never for a response sent to a client. <see langword="false"/> writes
    /// <c>"The server could not complete the request."</c> as the message of a fault of the system
    /// (<see cref="ErrorCategory.Technical"/>, <see cref="ErrorCategory.External"/>, <see cref="ErrorCategory.Unavailable"/>,
    /// <see cref="ErrorCategory.Timeout"/>, <see cref="ErrorCategory.NotSupported"/>, undefined values). Values come back as JSON primitives: an integer as a <see cref="long"/> (or a <see cref="ulong"/>), a non-integer as a <see cref="double"/>, a
    /// <see cref="Guid"/> or a <see cref="DateTime"/> as a <see cref="string"/>, any other object as its invariant string.
    /// </param>
    public ResultJsonConverter(bool includeMetadata) => _includeMetadata = includeMetadata;

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
            ErrorJson.Write(writer, value.Error, _includeMetadata);
        }
        writer.WriteEndObject();
    }
}
