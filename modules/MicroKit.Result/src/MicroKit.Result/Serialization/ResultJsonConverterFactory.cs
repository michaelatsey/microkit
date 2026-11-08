using System.Text.Json;
using System.Text.Json.Serialization;

namespace MicroKit.Result.Serialization;

/// <summary>
/// Factory that creates the converters of <see cref="Result{T}"/> and <see cref="Result"/>. Register it with
/// <see cref="JsonSerializerOptions.Converters"/> to enable result serialization.
/// </summary>
/// <remarks>
/// By default the converters never write <see cref="IError.Metadata"/>, which is in-process context and never sent to
/// a client (ADR-RESULT-001 D1, D12), and write a generic message for a fault of the system, whose message may
/// describe it: the <see cref="ResultJsonView.Client"/> view. Create the factory with <see cref="ResultJsonView.Full"/>
/// only for in-process storage, such as a cache, whose payload never reaches a client.
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
    private readonly ResultJsonView _view;

    /// <summary>Initializes a factory whose converters write the <see cref="ResultJsonView.Client"/> view.</summary>
    public ResultJsonConverterFactory() : this(ResultJsonView.Client)
    {
    }

    /// <summary>Initializes a factory.</summary>
    /// <param name="view">
    /// <see cref="ResultJsonView.Client"/> for anything a client may read; <see cref="ResultJsonView.Full"/> only for
    /// in-process storage, such as a cache, whose payload never reaches a client.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="view"/> is not a defined <see cref="ResultJsonView"/>.</exception>
    public ResultJsonConverterFactory(ResultJsonView view) => _view = ResultGuard.DefinedView(view);

    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert == typeof(Result)
        || (typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Result<>));

    /// <inheritdoc/>
    public override JsonConverter CreateConverter(
        Type typeToConvert, JsonSerializerOptions options)
    {
        if (typeToConvert == typeof(Result))
            return new ResultJsonConverter(_view);

        var valueType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(ResultJsonConverter<>).MakeGenericType(valueType);
        // The ! is safe: CreateInstance of a class never returns null.
        return (JsonConverter)Activator.CreateInstance(converterType, [_view])!;
    }
}
