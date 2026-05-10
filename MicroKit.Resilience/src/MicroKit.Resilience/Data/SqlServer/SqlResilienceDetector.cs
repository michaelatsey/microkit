using MicroKit.Resilience.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MicroKit.Resilience.Data.SqlServer;

public class SqlResilienceDetector : IResilienceStrategyDetector
{
    public bool ShouldRetry(Exception ex)
    {
        if (TryExtractSqlException(ex, out var sqlEx))
        {
            return IsSqlTransient(sqlEx?.Number);
        }
        return false;
    }
    public bool CanHandle(Exception ex) 
        => ex is SqlException || ex.InnerException is SqlException;
    private static bool IsSqlTransient(int? errorNumber)
    {
        // Liste des codes SQL Server considérés comme "transients"
        return errorNumber is
            1205 or  // Deadlock victim
            40613 or // Database unavailable
            40501 or // Service busy
            49918 or // Process limit reached
            49919;   // Process limit reached
    }
    private bool TryExtractSqlException(Exception ex, out SqlException? sqlException)
    {
        sqlException = ex as SqlException;
        if (sqlException != null) return true;

        // Si c'est du EF Core, on regarde à l'intérieur
        if (ex is DbUpdateException dbEx && dbEx.InnerException is SqlException innerEx)
        {
            sqlException = innerEx;
            return true;
        }

        // Cas générique (InnerException)
        if (ex.InnerException is SqlException inner)
        {
            sqlException = inner;
            return true;
        }

        return false;
    }


}
