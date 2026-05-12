using MicroKit.Messaging.Abstractions.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MicroKit.Messaging.Persistence.EFCore.Configurations.Outbox;

/// <summary>EF Core entity type configuration for <see cref="OutboxMessage"/> that delegates to a provider-specific implementation.</summary>
public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    private readonly string? _providerName;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="providerName">The EF Core provider name used to select the appropriate column configuration.</param>
    public OutboxMessageConfiguration(string? providerName)
    {
        _providerName = providerName;
    }

    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        var isPostgreSql = _providerName == "Npgsql.EntityFrameworkCore.PostgreSQL";

        _ = isPostgreSql == true ? builder.PostgreSQLConfigure() 
                : builder.SQLServerConfigure();
    }
}
