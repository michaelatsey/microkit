using MicroKit.Security.Abstractions.Enums;
using MicroKit.Security.Cognito.Options;
using MicroKit.Security.Cognito.Providers;
using MicroKit.Security.Core.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.Security.Cognito.DependencyInjection;

/// <summary>Extension methods for adding AWS Cognito authentication.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Adds Cognito token validation to the security pipeline.</summary>
    /// <param name="builder">The security builder.</param>
    /// <param name="configuration">Application configuration used to bind <see cref="CognitoOptions"/>.</param>
    /// <param name="configure">Optional additional configuration delegate.</param>
    /// <param name="useCache">When <see langword="true"/>, enables two-level caching of authentication results.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static SecurityBuilder AddCognito(
        this SecurityBuilder builder,
        IConfiguration configuration,
        Action<CognitoOptions>? configure = null,
        bool useCache = false)
    {
        builder.Services
            .AddOptions<CognitoOptions>()
            .Bind(configuration.GetSection(CognitoOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        if (configure is not null)
            builder.Services.Configure(configure);

        var scheme = AuthenticationScheme.Cognito.ToString();
        return builder.AddProvider<CognitoAuthenticationProvider, CognitoOptions>(scheme, enableCache: useCache);
    }
}
