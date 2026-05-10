using Microsoft.EntityFrameworkCore;

namespace MicroKit.Messaging.Persistence.EFCore.Extensions;

public static class DbUpdateExceptionExtensions
{
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
