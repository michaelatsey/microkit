using System.Data.Common;
using Npgsql;

namespace MicroKit.Persistence.EntityFrameworkCore.PostgreSql;

/// <summary>
/// Recognises PostgreSQL's unique (<c>23505</c>) and foreign-key (<c>23503</c>) violations by their
/// SQLSTATE, for the <see cref="IResultUnitOfWork"/> path.
/// </summary>
/// <remarks>
/// <c>UsePostgreSQL</c> configures it. For a context configured another way:
/// <code>
/// options.UseNpgsql(connectionString).UseMicroKitPersistence(PostgreSqlFailureClassifier.Instance);
/// </code>
/// A check violation (<c>23514</c>) and every other error stay unconverted.
/// </remarks>
public sealed class PostgreSqlFailureClassifier : IDatabaseFailureClassifier
{
    private PostgreSqlFailureClassifier()
    {
    }

    /// <summary>Gets the classifier; it is stateless.</summary>
    public static PostgreSqlFailureClassifier Instance { get; } = new();

    /// <inheritdoc/>
    public DatabaseFailure Classify(DbException exception) => exception switch
    {
        PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } => DatabaseFailure.UniqueViolation,
        PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation } => DatabaseFailure.ForeignKeyViolation,
        _ => DatabaseFailure.None,
    };
}
