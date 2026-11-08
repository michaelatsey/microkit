using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace MicroKit.Result.Serialization;

/// <summary>
/// System.Text.Json converter for <see cref="Result{T}"/>: <c>{"isSuccess":true,"value":…}</c> or
/// <c>{"isSuccess":false,"error":{…}}</c>.
/// </summary>
/// <remarks>
/// By default it writes the <see cref="ResultJsonView.Client"/> view, which that member documents: what a client may
/// read. Created with <see cref="ResultJsonView.Full"/>, the error object holds <c>code</c>, <c>message</c>,
/// <c>category</c>, <c>severity</c>, <c>propertyName</c> for a validation error, <c>errors</c> for an
/// <see cref="ErrorCollection"/>, and <c>metadata</c> (ADR-RESULT-001 D12). Reading rebuilds an <see cref="ErrorCollection"/>, a
/// <see cref="DeserializedValidationError"/> or a <see cref="DeserializedError"/>. A payload that does not describe a
/// valid result fails with <see cref="JsonException"/>, and writing a <c>default</c> result too.
/// <para>
/// The converter uses no reflection: it reads and writes the value through the contract the options resolve,
/// <c>options.GetTypeInfo(typeof(T))</c>, so it is trim and NativeAOT safe. Under source generation, register it
/// next to a <see cref="JsonSerializerContext"/> that declares both <c>Result&lt;T&gt;</c> and <typeparamref name="T"/>
/// (<c>[JsonSourceGenerationOptions(Converters = [typeof(ResultJsonConverter&lt;OrderDto&gt;)])]</c>).
/// </para>
/// </remarks>
/// <typeparam name="T">The result value type.</typeparam>
public sealed class ResultJsonConverter<T> : JsonConverter<Result<T>>
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
                    value = JsonSerializer.Deserialize(ref reader, GetValueTypeInfo(options));
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
        Utf8JsonWriter writer, Result<T> value, JsonSerializerOptions options) =>
        WriteEnvelope(writer, value, options, _view);

    // Also the writer Result<T> carries by its own [JsonConverter], with no converter registered (never metadata).
    internal static void WriteEnvelope(
        Utf8JsonWriter writer, Result<T> value, JsonSerializerOptions options, ResultJsonView view)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(options);
        if (!value.IsSuccess && !value.IsFailure)
            throw new JsonException("Cannot serialize an uninitialized (default) Result<T>: it is neither a success nor a failure.");

        writer.WriteStartObject();
        writer.WriteBoolean("isSuccess", value.IsSuccess);

        if (value.IsSuccess)
        {
            writer.WritePropertyName("value");
            JsonSerializer.Serialize(writer, value.Value, GetValueTypeInfo(options));
        }
        else
        {
            writer.WritePropertyName("error");
            ErrorJson.Write(writer, value.Error, view);
        }

        writer.WriteEndObject();
    }

    // The contract the options resolve (a source-generated context, or reflection where it is enabled): never
    // reflection of its own (Microsoft Learn, "How to use source generation in System.Text.Json").
    private static JsonTypeInfo<T> GetValueTypeInfo(JsonSerializerOptions options) =>
        (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
}
