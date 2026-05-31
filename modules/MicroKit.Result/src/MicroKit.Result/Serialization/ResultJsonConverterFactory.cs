using System.Text.Json;
using System.Text.Json.Serialization;

namespace MicroKit.Result.Serialization;

/// <summary>
/// Factory that creates <see cref="ResultJsonConverter{T}"/> instances for any <see cref="Result{T}"/>.
/// Register with <see cref="JsonSerializerOptions.Converters"/> to enable Result serialization.
/// </summary>
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
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType &&
        typeToConvert.GetGenericTypeDefinition() == typeof(Result<>);

    /// <inheritdoc/>
    public override JsonConverter CreateConverter(
        Type typeToConvert, JsonSerializerOptions options)
    {
        var valueType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(ResultJsonConverter<>).MakeGenericType(valueType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}
