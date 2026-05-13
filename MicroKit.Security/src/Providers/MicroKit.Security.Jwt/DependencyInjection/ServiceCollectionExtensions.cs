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
        var optionsBuilder = builder.Services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o =>
            {
                if (o.Signing.Algorithm.StartsWith("HS"))
                    return !string.IsNullOrEmpty(o.Signing.SecretKey);

                if (o.Signing.Algorithm.StartsWith("RS") || o.Signing.Algorithm.StartsWith("PS"))
                    return !string.IsNullOrEmpty(o.Signing.PublicKey) || !string.IsNullOrEmpty(o.Signing.PrivateKey);

                return false;
            }, "MicroKit.Security.Jwt: The configured algorithm requires a matching SecretKey or RSA Key in 'Signing' section.")
            .ValidateOnStart();

        if (configure is not null)
            builder.Services.Configure(configure);

        AddJwtCore(builder.Services);
        return builder;
    }

    private static void AddJwtCore(IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IJwtTokenService, JwtTokenService>();
    }
}
