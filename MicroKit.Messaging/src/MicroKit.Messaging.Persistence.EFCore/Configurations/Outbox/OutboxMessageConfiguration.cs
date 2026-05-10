using MicroKit.Messaging.Abstractions.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MicroKit.Messaging.Persistence.EFCore.Configurations.Outbox;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    private readonly string? _providerName;

    public OutboxMessageConfiguration(string? providerName)
    {
        _providerName = providerName;
    }

    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        var isPostgreSql = _providerName == "Npgsql.EntityFrameworkCore.PostgreSQL";

        _ = isPostgreSql == true ? builder.PostgreSQLConfigure() 
                : builder.SQLServerConfigure();
    }
}
