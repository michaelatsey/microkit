
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

public static class JwtAspNetCoreExtensions
{
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
