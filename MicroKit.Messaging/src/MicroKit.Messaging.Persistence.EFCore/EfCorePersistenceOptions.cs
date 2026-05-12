using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace MicroKit.Messaging.Persistence.EFCore;

/// <summary>Options for configuring the EF Core database provider used by the messaging persistence layer.</summary>
public class EfCorePersistenceOptions
{
    /// <summary>Gets the action used to configure the <see cref="DbContextOptionsBuilder"/>.</summary>
    public Action<DbContextOptionsBuilder>? DbContextOptionsAction { get; private set; }

    /// <summary>Configures the persistence layer to use SQL Server with the given connection string.</summary>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <param name="sqlServerOptionsAction">Optional SQL Server-specific options.</param>
    public void UseSqlServer(string? connectionString, Action<SqlServerDbContextOptionsBuilder>? sqlServerOptionsAction = null)
    {
        DbContextOptionsAction = options => options.UseSqlServer(connectionString, sqlServerOptionsAction);
    }

    /// <summary>Configures the persistence layer to use PostgreSQL with the given connection string.</summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="npgsqlOptionsAction">Optional Npgsql-specific options.</param>
    public void UseNpgsql(string connectionString, Action<NpgsqlDbContextOptionsBuilder>? npgsqlOptionsAction = null)
    {
        DbContextOptionsAction = options => options.UseNpgsql(connectionString, npgsqlOptionsAction);
    }

    // Tu peux ajouter UseSqlite, etc.
}
