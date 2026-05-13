using MicroKit.Resilience.Abstractions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MicroKit.Resilience.Data.PostgreSQL;

/// <summary>
/// Resilience detector for PostgreSQL transient errors.
/// </summary>
/// <remarks>
/// This detector identifies transient PostgreSQL exceptions and database exceptions
/// that occur when using Entity Framework Core, determining whether they should
/// be retried.
/// </remarks>
public sealed class PostgresResilienceDetector : IResilienceStrategyDetector
{
    /// <summary>
    /// Determines whether the specified exception originates from PostgreSQL.
    /// </summary>
    /// <param name="ex">The exception to evaluate.</param>
    /// <returns>
    /// <c>true</c> if the exception is a PostgreSQL exception or contains one; otherwise, <c>false</c>.
    /// </returns>
    public bool CanHandle(Exception ex)
        => ex is NpgsqlException || ex.InnerException is NpgsqlException;

    /// <summary>
    /// Determines whether a PostgreSQL exception represents a transient error
    /// that should be retried.
    /// </summary>
    /// <param name="ex">The exception to evaluate.</param>
    /// <returns>
    /// <c>true</c> if the exception represents a transient PostgreSQL error; otherwise, <c>false</c>.
    /// </returns>
    public bool ShouldRetry(Exception ex)
    {
        if (TryExtractPostgresException(ex, out var pgEx))
        {
            return IsPostgresTransient(pgEx?.SqlState);
        }
        return false;
    }

    /// <summary>
    /// Determines whether a PostgreSQL error code represents a transient condition.
    /// </summary>
    /// <param name="sqlState">The PostgreSQL SQLSTATE code (5 characters).</param>
    /// <returns>
    /// <c>true</c> if the error is transient; otherwise, <c>false</c>.
    /// </returns>
    private static bool IsPostgresTransient(string? sqlState)
    {
        // PostgreSQL SQLSTATE codes for transient errors
        return sqlState switch
        {
            // Class 08 - Connection Exception
            "08000" or // sqlclient_unable_to_establish_sqlconnection
            "08003" or // connection_does_not_exist
            "08006" or // connection_failure

            // Class 53 - Insufficient Resources
            "53000" or // insufficient_resources
            "53100" or // disk_full
            "53200" or // out_of_memory
            "53300" or // too_many_connections

            // Class 57 - Operator Intervention
            "57014" => true, // query_canceled

            _ => false
        };
    }

    /// <summary>
    /// Extracts a PostgreSQL exception from the given exception or its inner exceptions.
    /// </summary>
    /// <param name="ex">The exception to examine.</param>
    /// <param name="postgresException">Output parameter containing the extracted PostgreSQL exception if found.</param>
    /// <returns>
    /// <c>true</c> if a PostgreSQL exception was successfully extracted; otherwise, <c>false</c>.
    /// </returns>
    private static bool TryExtractPostgresException(Exception ex, out NpgsqlException? postgresException)
    {
        postgresException = ex as NpgsqlException;
        if (postgresException != null)
            return true;

        // Check if it's an Entity Framework DbUpdateException wrapping a PostgreSQL exception
        if (ex is DbUpdateException dbEx && dbEx.InnerException is NpgsqlException innerEx)
        {
            postgresException = innerEx;
            return true;
        }

        // Generic case: check InnerException
        if (ex.InnerException is NpgsqlException inner)
        {
            postgresException = inner;
            return true;
        }

        return false;
    }
}
