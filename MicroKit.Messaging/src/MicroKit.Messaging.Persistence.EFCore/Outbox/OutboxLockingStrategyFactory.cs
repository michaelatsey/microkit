using MicroKit.Messaging.Persistence.EFCore.Outbox.Locking;
using Microsoft.EntityFrameworkCore;

namespace MicroKit.Messaging.Persistence.EFCore.Outbox;

public sealed class OutboxLockingStrategyFactory
{
    private readonly DbContext _dbContext;

    public OutboxLockingStrategyFactory(
        DbContext dbContext)
    {
        _dbContext = dbContext;
    }

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
