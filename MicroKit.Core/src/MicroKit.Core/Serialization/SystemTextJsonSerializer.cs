using MicroKit.Abstractions.Configuration;
using MicroKit.Abstractions.Serialization;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MicroKit.Core.Serialization;

/// <summary>
/// Réprésente une implémentation de sérialiseur de messages utilisant System.Text.Json, permettant de convertir des objets en format JSON et vice versa, en utilisant les options de sérialisation configurées pour personnaliser le comportement du sérialiseur JSON, telles que la politique de nommage des propriétés, l'indentation du JSON et les conditions d'ignorance des propriétés nulles lors de la sérialisation des messages dans le système de messagerie.
/// </summary>
/// <seealso cref="IMicroKitSerializer" />
public class SystemTextJsonSerializer : IMicroKitSerializer
{
    /// <summary>
    /// The options
    /// </summary>
    private readonly JsonSerializerOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemTextJsonSerializer"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    public SystemTextJsonSerializer(IOptions<SerializationOptions> options)
    {
        _options = options.Value.JsonSerializerOptions ?? new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    /// <summary>
    /// Serializes the specified value.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="value">The value.</param>
    /// <returns></returns>
    public string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, _options);
    }

    /// <summary>
    /// Deserializes the specified json.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="json">The json.</param>
    /// <returns></returns>
    public T? Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, _options);
    }

    /// <summary>
    /// Deserializes the specified json.
    /// </summary>
    /// <param name="json">The json.</param>
    /// <param name="type">The type.</param>
    /// <returns></returns>
    public object? Deserialize(string json, Type type)
    {
        return JsonSerializer.Deserialize(json, type, _options);
    }
}
