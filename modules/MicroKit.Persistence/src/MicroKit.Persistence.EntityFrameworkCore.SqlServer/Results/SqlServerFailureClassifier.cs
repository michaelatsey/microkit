using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace MicroKit.Persistence.EntityFrameworkCore.SqlServer;

/// <summary>
/// Recognises SQL Server's unique violations (<c>2627</c>, <c>2601</c>) and its error <c>547</c> by their
/// number, for the <see cref="IResultUnitOfWork"/> path.
/// </summary>
/// <remarks>
/// <para>
/// <c>547</c> is reported for a foreign-key violation and for a check violation alike: it is classified
/// <see cref="DatabaseFailure.ForeignKeyOrCheckViolation"/>, and <see cref="EfUnitOfWork{TContext}"/> maps
/// it to <c>REFERENCE</c> only when no entity type mapped to the failing entries' tables declares a check
/// constraint in the model. A check constraint that exists in the database but not in the model cannot be
/// seen: its violation would be reported as <c>REFERENCE</c>.
/// </para>
/// <para>
/// <c>UseSqlServer</c> configures it. For a context configured another way:
/// <code>
/// options.UseSqlServer(connectionString).UseMicroKitPersistence(SqlServerFailureClassifier.Instance);
/// </code>
/// </para>
/// </remarks>
public sealed class SqlServerFailureClassifier : IDatabaseFailureClassifier
{
    private const int UniqueConstraintViolation = 2627;
    private const int UniqueIndexViolation = 2601;
    private const int ConstraintConflict = 547;

    private SqlServerFailureClassifier()
    {
    }

    /// <summary>Gets the classifier; it is stateless.</summary>
    public static SqlServerFailureClassifier Instance { get; } = new();

    /// <inheritdoc/>
    public DatabaseFailure Classify(DbException exception) => exception switch
    {
        SqlException { Number: UniqueConstraintViolation or UniqueIndexViolation } => DatabaseFailure.UniqueViolation,
        SqlException { Number: ConstraintConflict } => DatabaseFailure.ForeignKeyOrCheckViolation,
        _ => DatabaseFailure.None,
    };
}
