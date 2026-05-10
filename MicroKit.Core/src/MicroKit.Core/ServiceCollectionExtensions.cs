using MicroKit.Abstractions.Configuration;
using MicroKit.Abstractions.Serialization;
using MicroKit.Abstractions.Services;
using MicroKit.Core.Serialization;
using MicroKit.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroKit.Core;

public static class ServiceCollectionExtensions
{
    public static MicroKitBuilder AddMicroKit(this IServiceCollection services, Action<MicroKitOptions>? configure = null )
    {
        services
            .AddOptions<MicroKitOptions>()
            .ValidateOnStart();

        // Register the default serializer. This can be overridden by the user if they want to use a different serializer.
        services.TryAddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.TryAddSingleton<IMicroKitSerializer, SystemTextJsonSerializer>();
        
        var builder = new MicroKitBuilder(services);
        builder.Configure(configure);
        

        return builder;
    }
}