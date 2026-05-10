using MicroKit.Idempotency.Abstractions.Contracts;
using MicroKit.Idempotency.Core.Configuration;
using MicroKit.Idempotency.EFCore.Configurations;
using MicroKit.Idempotency.EFCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroKit.Idempotency.EFCore;

public static class DependencyInjection
{
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
