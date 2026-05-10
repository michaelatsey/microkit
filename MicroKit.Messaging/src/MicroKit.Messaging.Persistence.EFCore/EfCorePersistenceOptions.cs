using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace MicroKit.Messaging.Persistence.EFCore;

public class EfCorePersistenceOptions
{
    // Stocke l'action de configuration (ex: options => options.UseSqlServer(...))
    public Action<DbContextOptionsBuilder>? DbContextOptionsAction { get; private set; }

    public void UseSqlServer(string? connectionString, Action<SqlServerDbContextOptionsBuilder>? sqlServerOptionsAction = null)
    {
        DbContextOptionsAction = options => options.UseSqlServer(connectionString, sqlServerOptionsAction);
    }

    public void UseNpgsql(string connectionString, Action<NpgsqlDbContextOptionsBuilder>? npgsqlOptionsAction = null)
    {
        DbContextOptionsAction = options => options.UseNpgsql(connectionString, npgsqlOptionsAction);
    }

    // Tu peux ajouter UseSqlite, etc.
}
