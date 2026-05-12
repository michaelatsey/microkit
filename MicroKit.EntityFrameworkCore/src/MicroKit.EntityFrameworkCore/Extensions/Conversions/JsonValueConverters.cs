using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace MicroKit.EntityFrameworkCore.Extensions.Conversions;

/// <summary>
/// Factory for EF Core <see cref="ValueConverter{TModel,TProvider}"/> instances that serialise
/// complex types to/from JSON columns.
/// </summary>
public static class JsonValueConverters
{
    /// <summary>
    /// Creates a <see cref="ValueConverter{TModel,TProvider}"/> that serialises <typeparamref name="T"/>
    /// to a JSON string and deserialises it back. Throws <see cref="InvalidOperationException"/> if
    /// the stored JSON deserialises to <see langword="null"/>.
    /// </summary>
    /// <typeparam name="T">The CLR type to persist as JSON.</typeparam>
    /// <param name="options">
    /// Optional <see cref="JsonSerializerOptions"/>. Defaults to <see cref="DefaultOptions"/> when
    /// <see langword="null"/>.
    /// </param>
    /// <returns>A configured <see cref="ValueConverter{TModel,TProvider}"/>.</returns>
    public static ValueConverter<T, string> Create<T>(
        JsonSerializerOptions? options = null)
    {
        options ??= DefaultOptions;

        return new ValueConverter<T, string>(
            v => JsonSerializer.Serialize(v, options),
            v => DeserializeOrThrow<T>(v, options));
    }

    private static T DeserializeOrThrow<T>(string value, JsonSerializerOptions options)
        => JsonSerializer.Deserialize<T>(value, options)
            ?? throw new InvalidOperationException(
                $"Deserialization of '{typeof(T).Name}' returned null.");

    /// <summary>
    /// Default <see cref="JsonSerializerOptions"/> used when none are supplied: camelCase property names,
    /// compact output.
    /// </summary>
    public static readonly JsonSerializerOptions DefaultOptions =
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
}
