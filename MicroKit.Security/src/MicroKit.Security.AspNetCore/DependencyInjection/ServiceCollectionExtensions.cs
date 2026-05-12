namespace MicroKit.Security.AspNetCore.DependencyInjection;

using MicroKit.Security.Abstractions.Contexts;
using MicroKit.Security.Abstractions.Options;
// using MicroKit.Security.AspNetCore.Authentication;
using MicroKit.Security.AspNetCore.Middleware;
// using MicroKit.Security.AspNetCore.Options;
using MicroKit.Security.AspNetCore.Services;
using MicroKit.Security.Core.Builder;
using MicroKit.Security.Core.DependencyInjection;
using MicroKit.Security.Core.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Extension methods for configuring MicroKit.Security with ASP.NET Core.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds MicroKit.Security ASP.NET Core integration.
    /// </summary>
    public static SecurityBuilder AddMicroKitSecurity(
        this IServiceCollection services,
        Action<SecurityOptions>? configure = null)
    {
        // Add core services first
        var builder = services.AddMicroKitSecurityCore();

        // Configure ASP.NET Core options
        if (configure is not null)
        {
            services.Configure(configure);
        }

        // Replace context accessor with HTTP-aware version
        services.AddHttpContextAccessor();
        services.Replace(ServiceDescriptor.Singleton<IClientContextAccessor, HttpClientContextAccessor>());


        return builder;
    }

    /// <summary>Adds MicroKit.Security ASP.NET Core integration with configuration binding.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration used to bind <see cref="SecurityOptions"/>.</param>
    /// <param name="configure">Optional additional configuration delegate.</param>
    public static SecurityBuilder AddMicroKitSecurity(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<SecurityOptions>? configure = null)
    {

        // Utilise la section définie dans les options (MicroKit:Security:ApiKey)
        var section = configuration.GetSection(SecurityOptions.SectionName);

        services.AddOptions<SecurityOptions>()
            .BindConfiguration(SecurityOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Configure ASP.NET Core options
        if (configure is not null)
        {
            services.Configure(configure);
        }

        // Add core services first
        var builder = services.AddMicroKitSecurityCore();

        // Replace context accessor with HTTP-aware version
        services.AddHttpContextAccessor();
        services.Replace(ServiceDescriptor.Singleton<IClientContextAccessor, HttpClientContextAccessor>());


        return builder;
    }

    ///// <summary>
    ///// Adds MicroKit authentication handler.
    ///// </summary>
    //public static AuthenticationBuilder AddMicroKitAuthentication(
    //    this AuthenticationBuilder builder,
    //    Action<MicroKitAuthenticationOptions>? configure = null)
    //{
    //    return builder.AddScheme<MicroKitAuthenticationOptions, MicroKitAuthenticationHandler>(
    //        MicroKitAuthenticationHandler.SchemeName,
    //        configure);
    //}

    ///// <summary>
    ///// Adds MicroKit authentication as default scheme.
    ///// </summary>
    //public static IServiceCollection AddMicroKitAuthentication(
    //    this IServiceCollection services,
    //    Action<MicroKitAuthenticationOptions>? configure = null)
    //{
    //    services.AddAuthentication(MicroKitAuthenticationHandler.SchemeName)
    //        .AddMicroKitAuthentication(configure);

    //    return services;
    //}
}

/// <summary>
/// Application builder extensions.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Adds MicroKit security middleware to the pipeline.
    /// </summary>
    public static IApplicationBuilder UseMicroKitSecurity(this IApplicationBuilder app)
    {
        return app.UseMiddleware<SecurityMiddleware>();
    }
}
