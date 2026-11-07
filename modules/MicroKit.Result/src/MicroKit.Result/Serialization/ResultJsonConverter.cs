using System.Text.Json;
using System.Text.Json.Serialization;

namespace MicroKit.Result.Serialization;

/// <summary>
/// System.Text.Json converter for <see cref="Result{T}"/>.
/// Serializes as <c>{"isSuccess":true,"value":...}</c> or
/// <c>{"isSuccess":false,"error":{"code":"...","message":"...","category":"..."}}</c>.
/// </summary>
/// <typeparam name="T">The result value type.</typeparam>
[RequiresUnreferencedCode("JSON serialization of Result<T> may require unreferenced code.")]
[RequiresDynamicCode("JSON serialization of Result<T> may require runtime code generation.")]
public sealed class ResultJsonConverter<T> : JsonConverter<Result<T>>
{
    /// <inheritdoc/>
    public override Result<T> Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected StartObject token.");

        bool? isSuccess = null;
        bool hasValue = false;
        bool hasError = false;
        T? value = default;
        string? errorCode = null;
        string? errorMessage = null;
        string? errorCategory = null;

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
                    if (hasError)
                        throw new JsonException("Duplicate 'error' property.");
                    ReadErrorObject(ref reader, out errorCode, out errorMessage, out errorCategory);
                    hasError = true;
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
            if (hasError)
                throw new JsonException("A successful result cannot carry an 'error' property.");
            return Result<T>.Success(value!);
        }

        if (!hasError)
            throw new JsonException("A failed result requires an 'error' property.");
        if (hasValue)
            throw new JsonException("A failed result cannot carry a 'value' property.");

        var category = Enum.TryParse<ErrorCategory>(errorCategory, true, out var parsed)
            ? parsed
            : ErrorCategory.Technical;

        ErrorCode code;
        try
        {
            code = ErrorCode.From(errorCode ?? "UNKNOWN");
        }
        catch (ArgumentException ex)
        {
            throw new JsonException("The error 'code' must not be empty.", ex);
        }

        return Result<T>.Failure(
            new DeserializedError(
                code,
                errorMessage ?? "Unknown error",
                category));
    }

    /// <inheritdoc/>
    public override void Write(
        Utf8JsonWriter writer, Result<T> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("isSuccess", value.IsSuccess);

        if (value.IsSuccess)
        {
            writer.WritePropertyName("value");
            JsonSerializer.Serialize(writer, value.Value, options);
        }
        else
        {
            writer.WriteStartObject("error");
            writer.WriteString("code", value.Error.Code.Value);
            writer.WriteString("message", value.Error.Message);
            writer.WriteString("category", value.Error.Category.ToString());
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static void ReadErrorObject(
        ref Utf8JsonReader reader,
        out string? code,
        out string? message,
        out string? category)
    {
        code = null;
        message = null;
        category = null;

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected error object.");

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            var prop = reader.GetString();
            reader.Read();

            switch (prop)
            {
                case "code": code = reader.GetString(); break;
                case "message": message = reader.GetString(); break;
                case "category": category = reader.GetString(); break;
                default: reader.Skip(); break;
            }
        }
    }

    private sealed record DeserializedError(ErrorCode Code, string Message, ErrorCategory Category)
        : Error(Code, Message)
    {
        public override ErrorCategory Category { get; } = Category;
    }
}
