namespace MicroKit.Persistence.Abstractions;

/// <summary>
/// Manages transaction lifecycle and exposes the current ambient transaction
/// to services that need to participate in or inspect an active transaction.
/// </summary>
/// <remarks>
/// <see cref="ITransactionManager"/> is registered as a scoped service alongside
/// <see cref="ITransactionalContext"/>. It allows cross-cutting infrastructure
/// (such as outbox pattern services) to access the active transaction without
/// holding a direct reference to the transaction object.
/// </remarks>
public interface ITransactionManager
{
    /// <summary>
    /// Gets the currently active transaction, or <see langword="null"/> if no
    /// transaction is in progress within the current scope.
    /// </summary>
    ITransaction? CurrentTransaction { get; }
}
