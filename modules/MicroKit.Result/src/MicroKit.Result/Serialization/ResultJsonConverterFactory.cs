using System.Text.Json;
using System.Text.Json.Serialization;

namespace MicroKit.Result.Serialization;

/// <summary>
/// Factory that creates the converters of <see cref="Result{T}"/> and <see cref="Result"/>. Register it with
/// <see cref="JsonSerializerOptions.Converters"/> to enable result serialization.
/// </summary>
/// <remarks>
/// By default the converters never write <see cref="IError.Metadata"/>, which is in-process context and never sent to
/// a client (ADR-RESULT-001 D1, D12). Create the factory with <c>includeMetadata: true</c> only for in-process
/// storage, such as a cache, whose payload never reaches a client.
/// </remarks>
/// <example>
/// <code>
/// var options = new JsonSerializerOptions();
/// options.Converters.Add(new ResultJsonConverterFactory());
/// </code>
/// </example>
[RequiresUnreferencedCode("JSON serialization of Result<T> may require unreferenced code.")]
[RequiresDynamicCode("Creating ResultJsonConverter<T> requires runtime code generation via MakeGenericType.")]
public sealed class ResultJsonConverterFactory : JsonConverterFactory
{
    private readonly bool _includeMetadata;

    /// <summary>Initializes a factory whose converters never write <see cref="IError.Metadata"/>.</summary>
    public ResultJsonConverterFactory() : this(includeMetadata: false)
    {
    }

    /// <summary>Initializes a factory.</summary>
    /// <param name="includeMetadata">
    /// <see langword="true"/> to write <see cref="IError.Metadata"/>, for in-process storage such as a cache; never for a
    /// response sent to a client. Values come back as JSON primitives: an integer as a <see cref="long"/> (or a <see cref="ulong"/>), a non-integer as a <see cref="double"/>, a
    /// <see cref="Guid"/> or a <see cref="DateTime"/> as a <see cref="string"/>, any other object as its invariant string.
    /// </param>
    public ResultJsonConverterFactory(bool includeMetadata) => _includeMetadata = includeMetadata;

    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert == typeof(Result)
        || (typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Result<>));

    /// <inheritdoc/>
    public override JsonConverter CreateConverter(
        Type typeToConvert, JsonSerializerOptions options)
    {
        if (typeToConvert == typeof(Result))
            return new ResultJsonConverter(_includeMetadata);

        var valueType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(ResultJsonConverter<>).MakeGenericType(valueType);
        // An array: a lone bool binds to CreateInstance(Type, bool nonPublic). The ! is safe: CreateInstance of a class
        // never returns null.
        return (JsonConverter)Activator.CreateInstance(converterType, [_includeMetadata])!;
    }
}
