
using MicroKit.Security.Abstractions.Authentication;
using MicroKit.Security.Abstractions.Authorization;
using MicroKit.Security.Abstractions.Contexts;
using MicroKit.Security.Abstractions.Options;
using MicroKit.Security.Abstractions.Validator;
using MicroKit.Security.Core.Authentication;
using MicroKit.Security.Core.Authorization;
using MicroKit.Security.Core.Builder;
using MicroKit.Security.Core.Contexts;
using MicroKit.Security.Core.Providers;
using MicroKit.Security.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MicroKit.Security.Core.Validation;

namespace MicroKit.Security.Core.DependencyInjection;
/// <summary>
/// Extension methods for configuring MicroKit.Security services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds MicroKit.Security core services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration action.</param>
    /// <returns>Security builder for additional configuration.</returns>
    public static SecurityBuilder AddMicroKitSecurityCore(
        this IServiceCollection services,
        Action<SecurityOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<IAuthenticationProviderFactory, AuthenticationProviderFactory>();
        services.TryAddScoped<IAuthenticationService, AuthenticationService>();
        services.TryAddSingleton<IAuthorizationService, AuthorizationService>();
        services.TryAddSingleton<ISecurityContextFactory, SecurityContextFactory>();

        services.TryAddEnumerable(ServiceDescriptor.Transient<ISecurityValidator, NoOpSecurityValidator>());

        services.TryAddScoped<IClientContextAccessor, ClientContextAccessor>();
        services.TryAddSingleton(TimeProvider.System);

        return new SecurityBuilder(services);
    }



    /// <summary>
    /// Adds caching support for authentication.
    /// </summary>
    public static SecurityBuilder WithDistributedCache(
        this SecurityBuilder builder,
        Action<CacheOptions>? configure = null)
    {
        if (configure != null) builder.Services.Configure(configure);

        builder.Services.Decorate<IAuthenticationProvider, CachedAuthenticationProvider>();

        return builder;
    }
}
