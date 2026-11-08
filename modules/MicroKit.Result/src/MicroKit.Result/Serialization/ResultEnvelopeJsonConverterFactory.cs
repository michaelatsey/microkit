using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MicroKit.Result.Serialization;

/// <summary>
/// Infrastructure for the <see cref="JsonConverterAttribute"/> of <see cref="Result{T}"/>; do not register it. With no
/// converter registered, a <see cref="Result{T}"/> is written as the documented envelope,
/// <c>{"isSuccess":true,"value":…}</c> or <c>{"isSuccess":false,"error":{…}}</c>, never with
/// <see cref="IError.Metadata"/> (ADR-RESULT-001 D12). A <see cref="Result{T}"/> returned as is by an endpoint is thus
/// never answered with a 500 for want of a converter.
/// </summary>
/// <remarks>
/// <para>
/// That output reaches clients by default: it is the <see cref="ResultJsonView.Client"/> view, the message of a fault
/// of the system (<see cref="ErrorCategoryExtensions.IsSystemFault"/>) being <see cref="ErrorCategoryExtensions.SystemFaultMessage"/>,
/// <c>code</c> and <c>category</c> kept. A returned result answers the status the endpoint gives it (200 for a plain
/// value):
/// for a client, map a failure to problem details with <c>ToHttpResult</c> (<c>MicroKit.Result.AspNetCore</c>).
/// </para>
/// <para>
/// It uses no reflection: the value is written through the contract the options resolve for it, so a
/// source-generated context must declare the value type. A converter registered in
/// <see cref="JsonSerializerOptions.Converters"/> takes precedence (<see cref="ResultJsonConverterFactory"/>,
/// <see cref="ResultJsonConverter{T}"/>).
/// </para>
/// <para>
/// Reading a <see cref="Result{T}"/> needs one of them: without it, it throws <see cref="JsonException"/>, as for any
/// payload this module cannot read, and the message names the fix. Register <see cref="ResultJsonConverterFactory"/>, or, under source generation and
/// Native AOT, <see cref="ResultJsonConverter{T}"/>. It is not meant to be registered itself.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ResultEnvelopeJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        return typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Result<>);
    }

    /// <inheritdoc/>
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        return new EnvelopeWriter(typeToConvert);
    }

    // Converts the boxed Result<T> through IResultEnvelope: System.Text.Json casts it to the result type, and the
    // result writes itself with T known statically.
    private sealed class EnvelopeWriter(Type resultType) : JsonConverter<IResultEnvelope>
    {
        public override IResultEnvelope Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var valueType = resultType.GetGenericArguments()[0].Name;
            // A JsonException, as for any payload this module cannot read: a reader that catches it (a cache, a
            // request binder) treats the entry as unreadable.
            throw new JsonException(
                $"Reading a Result<{valueType}> needs a converter: register ResultJsonConverterFactory in " +
                "JsonSerializerOptions.Converters (ConfigureHttpJsonOptions for Minimal APIs, AddJsonOptions for MVC), " +
                $"or, under source generation and Native AOT, ResultJsonConverter<{valueType}> in the context's " +
                "[JsonSourceGenerationOptions(Converters = ...)]. If ResultEnvelopeJsonConverterFactory is registered, " +
                "replace it: it only writes.");
        }

        public override void Write(Utf8JsonWriter writer, IResultEnvelope value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(value);
            value.WriteJson(writer, options);
        }
    }
}
