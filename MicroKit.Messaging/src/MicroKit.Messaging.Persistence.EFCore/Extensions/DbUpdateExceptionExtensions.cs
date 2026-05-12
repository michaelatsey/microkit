using Microsoft.EntityFrameworkCore;

namespace MicroKit.Messaging.Persistence.EFCore.Extensions;

/// <summary>Extension methods for detecting specific EF Core database update exception types.</summary>
public static class DbUpdateExceptionExtensions
{
    /// <summary>Returns <see langword="true"/> when the exception represents a unique constraint violation on SQL Server or PostgreSQL.</summary>
    /// <param name="ex">The <see cref="DbUpdateException"/> to inspect.</param>
    /// <returns><see langword="true"/> if this is a unique/duplicate key violation; otherwise <see langword="false"/>.</returns>
    public static bool IsUniqueConstraintViolation(this DbUpdateException ex)
    {
        // PostgreSQL (Npgsql)
        if (ex.InnerException is Microsoft.EntityFrameworkCore.DbUpdateException ||
            ex.InnerException?.GetType().Name == "PostgresException")
        {
            // Le code 23505 est le standard Postgres pour "unique_violation"
            dynamic postgresEx = ex.InnerException;
            return postgresEx.SqlState == "23505";
        }

        // SQL Server (Microsoft.Data.SqlClient)
        if (ex.InnerException?.GetType().Name == "SqlException")
        {
            dynamic sqlEx = ex.InnerException;
            return sqlEx.Number == 2601 || sqlEx.Number == 2627;
        }

        return false;
    }
}
