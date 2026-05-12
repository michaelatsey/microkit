using MicroKit.Messaging.Abstractions.Inbox;
using MicroKit.Messaging.Abstractions.Outbox;
using MicroKit.Messaging.Abstractions.Persistence;
using MicroKit.Messaging.Core.Configuration;
using MicroKit.Messaging.Core.Extensions.Persistence;
using MicroKit.Messaging.Persistence.EFCore.Inbox;
using MicroKit.Messaging.Persistence.EFCore.Inbox.Locking;
using MicroKit.Messaging.Persistence.EFCore.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroKit.Messaging.Persistence.EFCore.Extensions;

/// <summary>Extension methods for registering EF Core messaging persistence services.</summary>
public static class EFCoreRegistrationExtensions
{
    /// <summary>Registers all EF Core-backed messaging repositories, fetchers, cleanup services, and locking strategies.</summary>
    /// <typeparam name="TContext">The <see cref="DbContext"/> that owns the messaging tables.</typeparam>
    /// <param name="builder">The messaging builder.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static MicroKitMessagingBuilder UseEfCorePersistence<TContext>(
        this MicroKitMessagingBuilder builder
        //,
        //Action<EfCorePersistenceOptions> configure
        )
        where TContext : DbContext
    {
        //var options = new EfCorePersistenceOptions();
        //configure(options);

        //if (options.DbContextOptionsAction == null)
        //{
        //    throw new InvalidOperationException("DbContext options must be configured.");
        //}
        // Enregistre le DbContext de l'utilisateur
        //builder.Services.AddDbContext<TContext>(options.DbContextOptionsAction);
        // builder.Services.AddDbContextFactory<TContext>(options.DbContextOptionsAction, ServiceLifetime.Singleton);
        
        // Enregistre le Unit of Work (lié au DbContext spécifique)
        builder.Services.TryAddScoped<IMessagingUnitOfWork, EfMessagingUnitOfWork<TContext>>();

        //  Enregistre les Repositories génériques
        builder.AddRepositories<EFOutboxRepository<TContext>, EFInboxMessageRepository<TContext>>();
        builder.AddRepositories<EFOutboxRepository<TContext>, EFInboxMessageRepository<TContext>>();
        builder.Services.AddScoped<IInboxStateRepository, EfInboxStateRepository<TContext>>();
        // Enregistre le Fetcher qui utilise le DbContext
        builder.Services?.TryAddScoped(typeof(IOutboxMessageFetcher), typeof(EfOutboxMessageFetcher<TContext>));
        builder.Services?.TryAddScoped(typeof(IInboxStateFetcher), typeof(EfInboxStateFetcher<TContext>));

        // Enregistre le CleanupService qui utilise le DbContext
        builder.Services?.TryAddScoped<IOutboxCleanupService, EfOutboxCleanupService<TContext>>();
        builder.Services?.TryAddScoped<IInboxCleanupService, EfInboxCleanupService<TContext>>();

        // Enregistre le StatisticsReader qui utilise le DbContext
        builder.Services?.TryAddScoped<IOutboxStatisticsReader, EfOutboxStatisticsReader<TContext>>();

        // Enregistre les stratégies de locking en fonction du provider
        builder.Services?.RegisterOutboxLockingStrategy();
        builder.Services?.RegisterInboxLockingStrategy<TContext>(); 
        //get DbContext priovider and register DbContext with the same options
        //var dbContextProvider = TContext.GetConstructor(new Type[] { typeof(DbContextOptions<TContext>) });
        return builder;
    }

    private static void RegisterOutboxLockingStrategy(this IServiceCollection services)
    {
        services?.TryAddScoped<OutboxLockingStrategyFactory>();
        services?.TryAddScoped(sp =>
        {
            var strategy = sp.GetRequiredService<OutboxLockingStrategyFactory>().Create();
            return strategy;
        });
    }

    private static void RegisterInboxLockingStrategy<TContext>(this IServiceCollection services)
            where TContext : DbContext
    {
        services?.AddScoped<IInboxLockingStrategy>(sp =>
        {
            using var scope = sp.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            var providerName = context.Database.ProviderName;

            return providerName switch
            {
                "Microsoft.EntityFrameworkCore.SqlServer" => new SqlServerInboxLockingStrategy(),
                "Npgsql.EntityFrameworkCore.PostgreSQL" => new PostgresInboxLockingStrategy(),

                // On peut ajouter Postgres ici plus tard
                _ => new OptimisticInboxLockingStrategy() // Fallback universel que nous avons validé
            };
        });
    }
}
