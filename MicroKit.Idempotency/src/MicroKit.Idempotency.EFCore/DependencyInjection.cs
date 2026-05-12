using MicroKit.Idempotency.Abstractions.Contracts;
using MicroKit.Idempotency.Core.Configuration;
using MicroKit.Idempotency.EFCore.Configurations;
using MicroKit.Idempotency.EFCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroKit.Idempotency.EFCore;

/// <summary>Extension methods for registering the EF Core idempotency store.</summary>
public static class DependencyInjection
{
    /// <summary>Registers the EF Core-backed idempotency store and cleanup service.</summary>
    /// <typeparam name="TContext">The <see cref="DbContext"/> that owns the idempotency table.</typeparam>
    /// <param name="builder">The idempotency builder.</param>
    /// <param name="configureOptions">Optional callback to configure EF Core idempotency options.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static MicroKitIdempotencyBuilder UseEFcore<TContext>(
        this MicroKitIdempotencyBuilder builder, 
        Action<EFCoreIdempotencyOptions>? configureOptions = null)
        where TContext : DbContext
    {
        builder.Services
            .AddOptions<EFCoreIdempotencyOptions>()
            .BindConfiguration("MicroKit:Indempotency:EFcore") // Optionnel : permet de binder depuis appsettings.json
            .Configure(options => configureOptions?.Invoke(options))
            .ValidateDataAnnotations() // Active les attributs [Range], [Required], etc.
            .ValidateOnStart();


        builder.Services.AddScoped<IIdempotencyStore, EfCoreIdempotencyStore<TContext>>();
        builder.Services?.TryAddScoped<IIdempotencyCleanupService, EFCoreIdempotencyCleanupService<TContext>>();

        return builder;
    }
}
