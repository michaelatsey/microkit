using MicroKit.Security.Abstractions.Enums;
using MicroKit.Security.AzureAd.Options;
using MicroKit.Security.AzureAd.Providers;
using MicroKit.Security.Core.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.Security.AzureAd.DependencyInjection;

/// <summary>Extension methods for adding Azure Active Directory authentication.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Adds Azure AD token validation to the security pipeline.</summary>
    /// <param name="builder">The security builder.</param>
    /// <param name="configuration">Application configuration used to bind <see cref="AzureAdOptions"/>.</param>
    /// <param name="configure">Optional additional configuration delegate.</param>
    /// <param name="useCache">When <see langword="true"/>, enables two-level caching of authentication results.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static SecurityBuilder AddAzureAd(
        this SecurityBuilder builder,
        IConfiguration configuration,
        Action<AzureAdOptions>? configure = null,
        bool useCache = false)
    {
        builder.Services
            .AddOptions<AzureAdOptions>()
            .Bind(configuration.GetSection(AzureAdOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        if (configure is not null)
            builder.Services.Configure(configure);

        var scheme = AuthenticationScheme.AzureAd.ToString();
        return builder.AddProvider<AzureAdAuthenticationProvider, AzureAdOptions>(scheme, enableCache: useCache);
    }
}
