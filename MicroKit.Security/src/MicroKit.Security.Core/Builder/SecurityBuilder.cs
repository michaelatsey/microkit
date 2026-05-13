
using MicroKit.Security.Abstractions.Cache;
using MicroKit.Security.Abstractions.Enums;
using MicroKit.Security.Abstractions.Options;
using MicroKit.Security.Core.Providers;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;

namespace MicroKit.Security.Core.Builder;
/// <summary>
/// Builder for configuring MicroKit.Security services.
/// </summary>
public sealed class SecurityBuilder(IServiceCollection services)
{
    /// <summary>
    /// Gets the service collection.
    /// </summary>
    public IServiceCollection Services { get; } = services;

    /// <summary>
    /// Configured default authentication scheme.
    /// </summary>
    public AuthenticationScheme DefaultScheme { get; set; } = AuthenticationScheme.ApiKey;

    /// <summary>
    /// Registers an authentication provider with optional caching.
    /// </summary>
    public SecurityBuilder AddProvider<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
        string schemeName,
        bool enableCache = false)
        where TProvider : class, IAuthenticationProvider
        where TOptions : class, ICacheableOptions
    {
        Services.TryAddScoped<TProvider>();

        if (enableCache)
        {
            // Bind named CacheOptions to the parent options so each scheme's cache settings are isolated.
            Services.AddOptions<CacheOptions>(schemeName)
                .Configure<IOptionsMonitor<TOptions>>((cacheOpt, parentMonitor) =>
                {
                    var source = parentMonitor.Get(schemeName).Cache;
                    cacheOpt.Enabled = source.Enabled;
                    cacheOpt.KeyPrefix = source.KeyPrefix;
                    cacheOpt.SuccessDurationSeconds = source.SuccessDurationSeconds;
                    cacheOpt.FailureDurationSeconds = source.FailureDurationSeconds;
                    cacheOpt.DefaultDurationSeconds = source.DefaultDurationSeconds;
                    cacheOpt.MaxCacheSize = source.MaxCacheSize;
                    cacheOpt.UseSlidingExpiration = source.UseSlidingExpiration;
                });
            Services.AddScoped<IAuthenticationProvider>(sp =>
            {
                var inner = sp.GetRequiredService<TProvider>();
                var optionsMonitor = sp.GetRequiredService<IOptionsMonitor<CacheOptions>>();
                var specificCacheOptions = optionsMonitor.Get(schemeName);

                return new CachedAuthenticationProvider(
                    inner,
                    sp.GetRequiredService<IMemoryCache>(),
                    sp.GetRequiredService<IDistributedCache>(),
                    // Wrap the scheme-specific options in a plain IOptions so CachedAuthenticationProvider stays decoupled.
                    Microsoft.Extensions.Options.Options.Create(specificCacheOptions),
                    sp.GetRequiredService<ILogger<CachedAuthenticationProvider>>()
                );
            });
        }
        else
        {
            Services.AddScoped<IAuthenticationProvider, TProvider>(sp => sp.GetRequiredService<TProvider>());
        }

        return this;
    }


}
