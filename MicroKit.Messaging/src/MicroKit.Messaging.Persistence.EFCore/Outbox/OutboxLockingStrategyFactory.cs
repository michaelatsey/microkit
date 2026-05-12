using MicroKit.Messaging.Persistence.EFCore.Outbox.Locking;
using Microsoft.EntityFrameworkCore;

namespace MicroKit.Messaging.Persistence.EFCore.Outbox;

/// <summary>Factory that selects the appropriate <see cref="IOutboxLockingStrategy"/> based on the configured EF Core provider.</summary>
public sealed class OutboxLockingStrategyFactory
{
    private readonly DbContext _dbContext;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="dbContext">The EF Core context used to determine the active database provider.</param>
    public OutboxLockingStrategyFactory(
        DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Creates the locking strategy appropriate for the current database provider.</summary>
    /// <returns>A provider-specific <see cref="IOutboxLockingStrategy"/> instance.</returns>
    public IOutboxLockingStrategy Create()
    {
        return _dbContext.Database.ProviderName switch
        {
            "Microsoft.EntityFrameworkCore.SqlServer"
                => new SqlServerSkipOutboxLockedStrategy(),

            "Npgsql.EntityFrameworkCore.PostgreSQL"
                => new PostgresSkipOutboxLockedStrategy(),

            _ => new OptimisticOutboxLockingStrategy()
        };
    }
}
