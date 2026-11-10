namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// What a provider's <see cref="IDatabaseFailureClassifier"/> recognises in a database error: the
/// failures the <see cref="IResultUnitOfWork"/> path can return as <see cref="PersistenceErrors"/>.
/// </summary>
public enum DatabaseFailure
{
    /// <summary>Not a failure the <see cref="IResultUnitOfWork"/> path converts.</summary>
    None = 0,

    /// <summary>A unique-constraint violation (PostgreSQL <c>23505</c>, SQL Server <c>2627</c>/<c>2601</c>).</summary>
    UniqueViolation = 1,

    /// <summary>A foreign-key violation (PostgreSQL <c>23503</c>).</summary>
    ForeignKeyViolation = 2,

    /// <summary>
    /// A foreign-key violation or a check-constraint violation, which the provider reports with one
    /// error number (SQL Server <c>547</c>). It is decided from the model: a foreign-key violation only
    /// when no entity type mapped to the failing entries' tables declares a check constraint.
    /// </summary>
    ForeignKeyOrCheckViolation = 3,
}
