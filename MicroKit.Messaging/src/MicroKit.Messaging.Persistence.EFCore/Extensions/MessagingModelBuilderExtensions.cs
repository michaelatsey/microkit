using MicroKit.Messaging.Persistence.EFCore.Configurations.Inbox;
using MicroKit.Messaging.Persistence.EFCore.Configurations.Outbox;
using Microsoft.EntityFrameworkCore;

namespace MicroKit.Messaging.Persistence.EFCore.Extensions;

public static class MessagingModelBuilderExtensions
{
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
