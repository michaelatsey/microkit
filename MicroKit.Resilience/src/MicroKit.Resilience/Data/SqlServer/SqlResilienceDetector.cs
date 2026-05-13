using MicroKit.Resilience.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MicroKit.Resilience.Data.SqlServer;

/// <summary>
/// Resilience detector for SQL Server transient errors.
/// </summary>
/// <remarks>
/// This detector identifies transient SQL Server exceptions and database exceptions
/// that occur when using Entity Framework Core, determining whether they should
/// be retried.
/// </remarks>
public sealed class SqlResilienceDetector : IResilienceStrategyDetector
{
    /// <summary>
    /// Determines whether the specified exception originates from SQL Server.
    /// </summary>
    /// <param name="ex">The exception to evaluate.</param>
    /// <returns>
    /// <c>true</c> if the exception is a SQL Server exception or contains one; otherwise, <c>false</c>.
    /// </returns>
    public bool CanHandle(Exception ex)
        => ex is SqlException || ex.InnerException is SqlException;

    /// <summary>
    /// Determines whether a SQL Server exception represents a transient error
    /// that should be retried.
    /// </summary>
    /// <param name="ex">The exception to evaluate.</param>
    /// <returns>
    /// <c>true</c> if the exception represents a transient SQL Server error; otherwise, <c>false</c>.
    /// </returns>
    public bool ShouldRetry(Exception ex)
    {
        if (TryExtractSqlException(ex, out var sqlEx))
        {
            return IsSqlTransient(sqlEx?.Number);
        }
        return false;
    }

    /// <summary>
    /// Determines whether a SQL Server error number represents a transient condition.
    /// </summary>
    /// <param name="errorNumber">The SQL Server error number.</param>
    /// <returns>
    /// <c>true</c> if the error is transient; otherwise, <c>false</c>.
    /// </returns>
    private static bool IsSqlTransient(int? errorNumber)
    {
        return errorNumber is
            1205 or  // Deadlock victim
            40613 or // Database unavailable
            40501 or // Service currently busy
            49918 or // Cannot process request (resource limit reached)
            49919;   // Cannot process request (too many create or update operations)
    }

    /// <summary>
    /// Extracts a SQL Server exception from the given exception or its inner exceptions.
    /// </summary>
    /// <param name="ex">The exception to examine.</param>
    /// <param name="sqlException">Output parameter containing the extracted SQL exception if found.</param>
    /// <returns>
    /// <c>true</c> if a SQL exception was successfully extracted; otherwise, <c>false</c>.
    /// </returns>
    private static bool TryExtractSqlException(Exception ex, out SqlException? sqlException)
    {
        sqlException = ex as SqlException;
        if (sqlException != null)
            return true;

        // Check if it's an Entity Framework DbUpdateException wrapping a SQL exception
        if (ex is DbUpdateException dbEx && dbEx.InnerException is SqlException innerEx)
        {
            sqlException = innerEx;
            return true;
        }

        // Generic case: check InnerException
        if (ex.InnerException is SqlException inner)
        {
            sqlException = inner;
            return true;
        }

        return false;
    }
}
