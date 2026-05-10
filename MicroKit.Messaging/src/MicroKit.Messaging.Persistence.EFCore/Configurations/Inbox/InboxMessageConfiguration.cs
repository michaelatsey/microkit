using MicroKit.Messaging.Abstractions.Inbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MicroKit.Messaging.Persistence.EFCore.Configurations.Inbox;

public class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    private readonly string? _providerName;

    public InboxMessageConfiguration(string? providerName)
    {
        this._providerName = providerName;
    }

    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        var isPostgreSql = _providerName == "Npgsql.EntityFrameworkCore.PostgreSQL";
        _ = isPostgreSql == true ? builder.ConfigurePostgreSQLInboxMessage()
                : builder.ConfigureSQLServerInboxMessage();
    }
}
