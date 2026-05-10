namespace MicroKit.Security.Jwt.DependencyInjection;

using MicroKit.Security.Core.Builder;
using MicroKit.Security.Core.Providers;
using MicroKit.Security.Jwt.Options;
using MicroKit.Security.Jwt.Providers;
using MicroKit.Security.Jwt.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Extension methods for adding JWT authentication.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the JWT.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="configure">The configure.</param>
    /// <returns>Security builder for chaining.</returns>
    public static SecurityBuilder AddJwt(
        this SecurityBuilder builder,
        IConfiguration configuration,
        Action<JwtOptions>? configure = null)
    {
        // 1. Liaison avec la section de configuration (appsettings.json)
        var optionsBuilder = builder.Services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o =>
            {
                // Utilisation du nouveau modèle d'options (o.Signing...)
                if (o.Signing.Algorithm.StartsWith("HS"))
                    return !string.IsNullOrEmpty(o.Signing.SecretKey);

                if (o.Signing.Algorithm.StartsWith("RS") || o.Signing.Algorithm.StartsWith("PS"))
                    return !string.IsNullOrEmpty(o.Signing.PublicKey) || !string.IsNullOrEmpty(o.Signing.PrivateKey);

                return false;
            }, "MicroKit.Security.Jwt: The configured algorithm requires a matching SecretKey or RSA Key in 'Signing' section.")
            .ValidateOnStart();

        // 2. Application de la configuration manuelle via Action (surcharge le JSON)
        if (configure != null)
        {
            builder.Services.Configure(configure);
        }

        AddJwtCore(builder.Services);
        return builder;
    }

    
    /// <summary>
    /// Centralizes service registration to ensure consistency.
    /// </summary>
    private static void AddJwtCore(IServiceCollection services)
    {
        // Nécessaire pour les tests et la gestion du temps dans JwtTokenService
        services.TryAddSingleton(TimeProvider.System);

        // Service de haut niveau pour générer/gérer les tokens
        services.TryAddSingleton<IJwtTokenService, JwtTokenService>();

    }
}
