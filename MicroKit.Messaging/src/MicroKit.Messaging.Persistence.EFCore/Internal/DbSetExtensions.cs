using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace MicroKit.Messaging.Persistence.EFCore.Internal;

internal static class DbSetExtensions
{
    public static string GetFullTableName<TEntity>(this DbSet<TEntity> dbSet)
        where TEntity : class
    {
        var entityType = dbSet.EntityType;
        var tableName = entityType.GetTableName();
        var schema = entityType.GetSchema();

        var context = dbSet.GetService<ICurrentDbContext>().Context;
        var provider = context.Database.ProviderName ?? string.Empty;

        if (provider.Contains("Npgsql"))
        {
            schema ??= "public";

            return $"\"{schema}\".\"{tableName}\"";
        }

        if (provider.Contains("SqlServer"))
        {
            schema ??= "dbo";

            return $"[{schema}].[{tableName}]";
        }

        // Fallback générique (MySQL, SQLite…)
        return string.IsNullOrEmpty(schema)
            ? tableName!
            : $"{schema}.{tableName}";
    }
}
