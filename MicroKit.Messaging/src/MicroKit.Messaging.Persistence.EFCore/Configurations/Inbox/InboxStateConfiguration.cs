using MicroKit.Messaging.Abstractions.Inbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MicroKit.Messaging.Persistence.EFCore.Configurations.Inbox;

/// <summary>EF Core entity type configuration for <see cref="InboxState"/> that delegates to a provider-specific implementation.</summary>
public class InboxStateConfiguration : IEntityTypeConfiguration<InboxState>
{
    private readonly string? _providerName;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="providerName">The EF Core provider name used to select the appropriate column configuration.</param>
    public InboxStateConfiguration(string? providerName)
    {
        this._providerName = providerName;
    }

    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<InboxState> builder)
    {
        var isPostgreSql = _providerName == "Npgsql.EntityFrameworkCore.PostgreSQL";
        _ = isPostgreSql == true ? builder.ConfigurePostgreSQInboxState()
                : builder.ConfigureSQLServerInboxState();
    }
}