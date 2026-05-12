using MicroKit.Messaging.Persistence.EFCore.Configurations.Inbox;
using MicroKit.Messaging.Persistence.EFCore.Configurations.Outbox;
using Microsoft.EntityFrameworkCore;

namespace MicroKit.Messaging.Persistence.EFCore.Extensions;

/// <summary>Extension methods for applying MicroKit messaging EF Core entity configurations.</summary>
public static class MessagingModelBuilderExtensions
{
    /// <summary>Applies outbox and inbox entity type configurations for the detected database provider.</summary>
    /// <param name="modelBuilder">The EF Core model builder.</param>
    /// <param name="context">The <see cref="DbContext"/> used to determine the active provider.</param>
    /// <returns>The same <paramref name="modelBuilder"/> for chaining.</returns>
    public static ModelBuilder ApplyMessagingConfigurations(this ModelBuilder modelBuilder, DbContext context)
    {
        // On récupère le nom du provider (SqlServer, Npgsql, etc.)
        var providerName = context.Database.ProviderName;

        // On applique les configurations internes
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration(providerName));
        modelBuilder.ApplyConfiguration(new InboxStateConfiguration(providerName));
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration(providerName));

        return modelBuilder;
    }
}
