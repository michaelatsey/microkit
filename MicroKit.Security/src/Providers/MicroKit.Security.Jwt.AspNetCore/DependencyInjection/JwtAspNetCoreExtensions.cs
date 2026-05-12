
using MicroKit.Security.Abstractions.Enums;
using MicroKit.Security.Abstractions.Options;
using MicroKit.Security.AspNetCore.Extraction;
using MicroKit.Security.Core.Builder;
using MicroKit.Security.Jwt.AspNetCore.Extraction;
using MicroKit.Security.Jwt.DependencyInjection;
using MicroKit.Security.Jwt.Options;
using MicroKit.Security.Jwt.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace MicroKit.Security.Jwt.AspNetCore.DependencyInjection;

/// <summary>Extension methods for registering JWT Bearer authentication with ASP.NET Core.</summary>
public static class JwtAspNetCoreExtensions
{
    /// <summary>Registers JWT Bearer authentication and the <see cref="JwtHeaderExtractor"/>.</summary>
    /// <param name="builder">The security builder.</param>
    /// <param name="configuration">The application configuration used to bind JWT options.</param>
    /// <param name="configure">Optional callback to configure <see cref="JwtOptions"/>.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static SecurityBuilder AddJwtAspNetCore(
        this SecurityBuilder builder,
        IConfiguration configuration,
        Action<JwtOptions>? configure = null)
    {
        // 1. Appel au code métier (Projet MicroKit.Security.Jwt)
        // Enregistre JwtOptions, IJwtTokenService et JwtAuthenticationProvider
        builder.AddJwt(configuration,configure);

        // 2. Enregistrement de l'extracteur de Header spécifique au Web
        // C'est ce qui permet au Middleware de "voir" le token dans les requêtes HTTP
        builder.Services.AddSingleton<IAuthenticationExtractor, JwtHeaderExtractor>();
        return builder;
    }

    /// <summary>Registers JWT Bearer authentication with optional caching and provider registration.</summary>
    /// <param name="builder">The security builder.</param>
    /// <param name="configuration">The application configuration used to bind JWT options.</param>
    /// <param name="optionsConfigure">Optional callback to configure <see cref="JwtOptions"/>.</param>
    /// <param name="useCache">When <see langword="true"/>, enables caching for authentication results.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static SecurityBuilder AddJwtAspNetCore(
        this SecurityBuilder builder,
        IConfiguration configuration,
        Action<JwtOptions>? optionsConfigure = null,
        bool useCache = false)
    {
        // 1. Appel au code métier (Projet MicroKit.Security.Jwt)
        // Enregistre JwtOptions, IJwtTokenService et JwtAuthenticationProvider
        builder.AddJwt(configuration,optionsConfigure);

        // 2. Enregistrement de l'extracteur de Header spécifique au Web
        // C'est ce qui permet au Middleware de "voir" le token dans les requêtes HTTP
        builder.Services.AddSingleton<IAuthenticationExtractor, JwtHeaderExtractor>();

        var scheme = AuthenticationScheme.Jwt.ToString();

        return builder.AddProvider<JwtAuthenticationProvider, JwtOptions>(scheme, enableCache: useCache);
    }
}
