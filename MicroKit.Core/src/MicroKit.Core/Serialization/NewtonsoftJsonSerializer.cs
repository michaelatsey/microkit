using MicroKit.Abstractions.Configuration;
using MicroKit.Abstractions.Serialization;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;

namespace MicroKit.Core.Serialization;

/// <summary>
/// <see cref="IMicroKitSerializer"/> implementation backed by Newtonsoft.Json.
/// Configured with camelCase naming, type-name handling for polymorphic types,
/// and null/reference-loop tolerance.
/// </summary>
public class NewtonsoftJsonSerializer : IMicroKitSerializer
{
    private readonly JsonSerializerSettings _settings;

    /// <summary>Initializes a new instance using the options provided by DI.</summary>
    /// <param name="options">Serialization options from the DI container.</param>
    public NewtonsoftJsonSerializer(IOptions<SerializationOptions> options)
    {
        _settings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
            TypeNameHandling = TypeNameHandling.Auto,
            NullValueHandling = NullValueHandling.Ignore,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore
        };
    }

    /// <inheritdoc />
    public string Serialize<T>(T value)
    {
        if (value == null) return string.Empty;
        return JsonConvert.SerializeObject(value, value.GetType(), _settings);
    }

    /// <inheritdoc />
    public T? Deserialize<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;
        return JsonConvert.DeserializeObject<T>(json, _settings);
    }

    /// <inheritdoc />
    public object? Deserialize(string json, Type type)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        return JsonConvert.DeserializeObject(json, type, _settings);
    }
}
