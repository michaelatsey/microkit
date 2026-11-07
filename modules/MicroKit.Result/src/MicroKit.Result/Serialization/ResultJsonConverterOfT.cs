using System.Text.Json;
using System.Text.Json.Serialization;

namespace MicroKit.Result.Serialization;

/// <summary>
/// System.Text.Json converter for <see cref="Result{T}"/>: <c>{"isSuccess":true,"value":…}</c> or
/// <c>{"isSuccess":false,"error":{…}}</c>.
/// </summary>
/// <remarks>
/// The error object holds <c>code</c>, <c>message</c>, <c>category</c>, <c>severity</c>, <c>propertyName</c> for a
/// validation error and <c>errors</c> for an <see cref="ErrorCollection"/>; <c>metadata</c> only when created with
/// <c>includeMetadata</c> (ADR-RESULT-001 D12). Reading rebuilds an <see cref="ErrorCollection"/>, a
/// <see cref="DeserializedValidationError"/> or a <see cref="DeserializedError"/>. A payload that does not describe a
/// valid result fails with <see cref="JsonException"/>, and writing a <c>default</c> result too.
/// </remarks>
/// <typeparam name="T">The result value type.</typeparam>
[RequiresUnreferencedCode("JSON serialization of Result<T> may require unreferenced code.")]
[RequiresDynamicCode("JSON serialization of Result<T> may require runtime code generation.")]
public sealed class ResultJsonConverter<T> : JsonConverter<Result<T>>
{
    private readonly bool _includeMetadata;

    /// <summary>Initializes a converter that never writes <see cref="IError.Metadata"/>.</summary>
    public ResultJsonConverter() : this(includeMetadata: false)
    {
    }

    /// <summary>Initializes a converter.</summary>
    /// <param name="includeMetadata">
    /// <see langword="true"/> to write <see cref="IError.Metadata"/>, for in-process storage such as a cache; never for a
    /// response sent to a client. Values come back as JSON primitives: an integer as a <see cref="long"/> (or a <see cref="ulong"/>), a non-integer as a <see cref="double"/>, a
    /// <see cref="Guid"/> or a <see cref="DateTime"/> as a <see cref="string"/>, any other object as its invariant string.
    /// </param>
    public ResultJsonConverter(bool includeMetadata) => _includeMetadata = includeMetadata;

    /// <inheritdoc/>
    public override Result<T> Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected StartObject token.");

        bool? isSuccess = null;
        bool hasValue = false;
        T? value = default;
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
                case "value":
                    if (hasValue)
                        throw new JsonException("Duplicate 'value' property.");
                    value = JsonSerializer.Deserialize<T>(ref reader, options);
                    hasValue = true;
                    break;
                case "error":
                    if (error is not null)
                        throw new JsonException("Duplicate 'error' property.");
                    error = ErrorJson.Read(ref reader);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        if (isSuccess is null)
            throw new JsonException("Missing 'isSuccess' property.");

        // A payload that does not describe a valid result fails: it is never read as a success.
        if (isSuccess.Value)
        {
            if (!hasValue)
                throw new JsonException("A successful result requires a 'value' property.");
            if (error is not null)
                throw new JsonException("A successful result cannot carry an 'error' property.");
            try
            {
                return Result<T>.Success(value!);
            }
            catch (ArgumentException ex) // a null value (ADR-RESULT-001 D6) or a result used as the value
            {
                throw new JsonException("The 'value' of a successful result must not be null or a result.", ex);
            }
        }

        if (error is null)
            throw new JsonException("A failed result requires an 'error' property.");
        if (hasValue)
            throw new JsonException("A failed result cannot carry a 'value' property.");

        return Result<T>.Failure(error);
    }

    /// <inheritdoc/>
    /// <exception cref="JsonException">Thrown when <paramref name="value"/> is uninitialized (<c>default</c>).</exception>
    public override void Write(
        Utf8JsonWriter writer, Result<T> value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (!value.IsSuccess && !value.IsFailure)
            throw new JsonException("Cannot serialize an uninitialized (default) Result<T>: it is neither a success nor a failure.");

        writer.WriteStartObject();
        writer.WriteBoolean("isSuccess", value.IsSuccess);

        if (value.IsSuccess)
        {
            writer.WritePropertyName("value");
            JsonSerializer.Serialize(writer, value.Value, options);
        }
        else
        {
            writer.WritePropertyName("error");
            ErrorJson.Write(writer, value.Error, _includeMetadata);
        }

        writer.WriteEndObject();
    }
}
