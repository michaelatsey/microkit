namespace MicroKit.Security.AspNetCore.DependencyInjection;

using MicroKit.Security.Abstractions.Contexts;
using MicroKit.Security.Abstractions.Options;
using MicroKit.Security.AspNetCore.Middleware;
using MicroKit.Security.AspNetCore.Services;
using MicroKit.Security.Core.Builder;
using MicroKit.Security.Core.DependencyInjection;
using MicroKit.Security.Core.Services;
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
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration delegate.</param>
    /// <returns>Security builder for chaining provider registrations.</returns>
    public static SecurityBuilder AddMicroKitSecurity(
        this IServiceCollection services,
        Action<SecurityOptions>? configure = null)
    {
        var builder = services.AddMicroKitSecurityCore();

        if (configure is not null)
            services.Configure(configure);

        services.AddHttpContextAccessor();
        services.Replace(ServiceDescriptor.Singleton<IClientContextAccessor, HttpClientContextAccessor>());

        return builder;
    }

    /// <summary>Adds MicroKit.Security ASP.NET Core integration with configuration binding.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration used to bind <see cref="SecurityOptions"/>.</param>
    /// <param name="configure">Optional additional configuration delegate.</param>
    /// <returns>Security builder for chaining provider registrations.</returns>
    public static SecurityBuilder AddMicroKitSecurity(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<SecurityOptions>? configure = null)
    {
        services.AddOptions<SecurityOptions>()
            .BindConfiguration(SecurityOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        if (configure is not null)
            services.Configure(configure);

        var builder = services.AddMicroKitSecurityCore();

        services.AddHttpContextAccessor();
        services.Replace(ServiceDescriptor.Singleton<IClientContextAccessor, HttpClientContextAccessor>());

        return builder;
    }
}

/// <summary>
/// Application builder extensions for MicroKit.Security middleware.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Adds MicroKit security middleware to the pipeline.
    /// Populates <see cref="IClientContextAccessor"/> and sets <see cref="Microsoft.AspNetCore.Http.HttpContext.User"/> on every request.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The same <paramref name="app"/> for chaining.</returns>
    public static IApplicationBuilder UseMicroKitSecurity(this IApplicationBuilder app)
    {
        return app.UseMiddleware<SecurityMiddleware>();
    }
}
