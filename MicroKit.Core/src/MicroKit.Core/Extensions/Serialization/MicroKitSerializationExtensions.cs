using MicroKit.Abstractions.Configuration;
using MicroKit.Abstractions.Serialization;
using MicroKit.Core.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroKit.Core.Extensions.Serialization;

public static class MicroKitSerializationExtensions
{
    public static MicroKitBuilder AddSystemTextJson(
        this MicroKitBuilder builder)
    {
        // On utilise TryAdd pour ne pas écraser les choix de l'utilisateur
        //Ou bien on pourrait faire un AddSingleton et laisser l'utilisateur écraser le service s'il le souhaite
        // Exemple : builder.Services.AddSingleton<IMicroKitSerializer, CustomSerializer>();
        builder.Services.TryAddSingleton<IMicroKitSerializer, SystemTextJsonSerializer>();
        return builder;
    }

    public static MicroKitBuilder AddNewtonsoftJson(
        this MicroKitBuilder builder)
    {
        // On utilise TryAdd pour ne pas écraser les choix de l'utilisateur
        //Ou bien on pourrait faire un AddSingleton et laisser l'utilisateur écraser le service s'il le souhaite
        // Exemple : builder.Services.AddSingleton<IMicroKitSerializer, CustomSerializer>();
        builder.Services.AddSingleton<IMicroKitSerializer, NewtonsoftJsonSerializer>();
        return builder;
    }
}
