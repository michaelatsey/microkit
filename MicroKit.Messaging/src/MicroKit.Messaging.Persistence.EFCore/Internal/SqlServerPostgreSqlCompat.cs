namespace MicroKit.Messaging.Persistence.EFCore.Internal;

internal static class SqlServerPostgreSqlCompat
{
    public static string GetStatusFilter(string? providerName, string statusValue)
    {
        // On gère le cas Postgres(Npgsql)
        if (providerName == "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            return $"\"Status\" = '{statusValue}'";
        }

        // Par défaut (SQL Server et autres), on utilise la syntaxe standard [Column]
        // ou simplement Column si on veut être encore plus générique.
        return $"[Status] = '{statusValue}'";
    }
}
