using MicroKit.Abstractions.Configuration;
using MicroKit.Abstractions.Serialization;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;

namespace MicroKit.Core.Serialization;

public class NewtonsoftJsonSerializer : IMicroKitSerializer
{
    private readonly JsonSerializerSettings _settings;

    public NewtonsoftJsonSerializer(IOptions<SerializationOptions> options)
    {
        // On récupère les settings ou on définit des défauts robustes
        _settings = new JsonSerializerSettings
        {
            // Crucial pour mapper "tableId" du JSON vers "TableId" du C#
            ContractResolver = new CamelCasePropertyNamesContractResolver(),

            // Permet de peupler les propriétés "get-only" via le constructeur
            ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,

            // Gère mieux l'héritage (NotificationEvent<IDomainEvent>)
            TypeNameHandling = TypeNameHandling.Auto,

            NullValueHandling = NullValueHandling.Ignore,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore
        };
    }

    public string Serialize<T>(T value)
    {
        if (value == null) return string.Empty;

        // On force Newtonsoft à regarder le type RÉEL de l'objet (GetType) 
        // plutôt que le type générique T, ce qui règle les problèmes de propriétés manquantes.
        return JsonConvert.SerializeObject(value, value.GetType(), _settings);
    }

    public T? Deserialize<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;
        return JsonConvert.DeserializeObject<T>(json, _settings);
    }

    public object? Deserialize(string json, Type type)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        return JsonConvert.DeserializeObject(json, type, _settings);
    }
}