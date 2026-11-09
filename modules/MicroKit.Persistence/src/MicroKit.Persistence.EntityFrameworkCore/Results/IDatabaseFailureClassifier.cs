using System.Data.Common;

namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// Recognises a provider's database errors for the <see cref="IResultUnitOfWork"/> path. The provider
/// packages implement it — <c>PostgreSqlFailureClassifier</c>, <c>SqlServerFailureClassifier</c> — so
/// that this package references no provider.
/// </summary>
/// <remarks>
/// <c>UsePostgreSQL</c> and <c>UseSqlServer</c> configure theirs. A context configured another way passes
/// one to <c>UseMicroKitPersistence(classifier)</c>; without one, only concurrency conflicts are converted
/// and unique or foreign-key violations propagate as <see cref="PersistenceException"/>.
/// <para>
/// An implementation is stateless and thread-safe: one instance sits in the options and serves every
/// context. It must not throw: it runs inside an exception filter, after the transaction is rolled back,
/// and an exception there is swallowed by the runtime, so the failure would silently not be converted.
/// </para>
/// </remarks>
public interface IDatabaseFailureClassifier
{
    /// <summary>Classifies a database error by its provider error code, never by its message.</summary>
    /// <param name="exception">The provider's exception.</param>
    /// <returns>The failure, or <see cref="DatabaseFailure.None"/>.</returns>
    DatabaseFailure Classify(DbException exception);
}
