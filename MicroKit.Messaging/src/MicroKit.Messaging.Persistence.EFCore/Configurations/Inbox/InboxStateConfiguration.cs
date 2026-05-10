using MicroKit.Messaging.Abstractions.Inbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MicroKit.Messaging.Persistence.EFCore.Configurations.Inbox;

public class InboxStateConfiguration : IEntityTypeConfiguration<InboxState>
{
    private readonly string? _providerName;

    public InboxStateConfiguration(string? providerName)
    {
        this._providerName = providerName;
    }

    public void Configure(EntityTypeBuilder<InboxState> builder)
    {
        var isPostgreSql = _providerName == "Npgsql.EntityFrameworkCore.PostgreSQL";
        _ = isPostgreSql == true ? builder.ConfigurePostgreSQInboxState()
                : builder.ConfigureSQLServerInboxState();
    }
}