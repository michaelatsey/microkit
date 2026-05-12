using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;

namespace MicroKit.EntityFrameworkCore.Extensions.Conversions;

/// <summary>
/// Extension methods for <see cref="PropertyBuilder{TProperty}"/> that configure JSON-backed column conversion.
/// </summary>
public static class JsonPropertyBuilderExtensions
{
    /// <summary>
    /// Configures the property to store its value as a JSON string and uses a structural value comparer
    /// so EF Core can detect modifications to complex types.
    /// </summary>
    /// <typeparam name="T">The CLR type of the property to persist as JSON.</typeparam>
    /// <param name="propertyBuilder">The property builder to configure.</param>
    /// <param name="options">Optional <see cref="JsonSerializerOptions"/>; defaults to <see cref="JsonValueConverters.DefaultOptions"/>.</param>
    /// <returns>The same <paramref name="propertyBuilder"/> for fluent chaining.</returns>
    public static PropertyBuilder<T> HasJsonConversion<T>(
        this PropertyBuilder<T> propertyBuilder,
        JsonSerializerOptions? options = null)
    {
        var converter = JsonValueConverters.Create<T>(options);

        var comparer = new ValueComparer<T>(
            (l, r) => JsonSerializer.Serialize(l, options) == JsonSerializer.Serialize(r, options),
            v => JsonSerializer.Serialize(v, options).GetHashCode(),
            v => JsonSerializer.Deserialize<T>(
                    JsonSerializer.Serialize(v, options), options)!);

        propertyBuilder.HasConversion(converter);
        propertyBuilder.Metadata.SetValueComparer(comparer);

        return propertyBuilder;
    }
}
