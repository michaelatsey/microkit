namespace MicroKit.Execution.Abstractions;

/// <summary>
/// Creates contextualized <see cref="IExecutionScope"/> instances for individual units
/// of work (one outbox message, one inbox message, one batch item).
/// </summary>
/// <remarks>
/// <para>
/// The factory is designed for dependency inversion: <c>MicroKit.Messaging</c> consumes
/// this contract; <c>MicroKit.Multitenancy</c> provides a tenant-aware implementation
/// that hydrates the per-tenant <c>DbContext</c> and <c>ITenantContext</c>. The default
/// pass-through implementation (in <c>MicroKit.Messaging</c> Core) simply wraps
/// <see cref="IServiceScopeFactory"/> with no additional hydration.
/// </para>
/// <para>
/// The method is <see langword="async"/> because future implementations may perform I/O
/// during scope creation (e.g. resolving a per-tenant connection string from a registry).
/// </para>
/// </remarks>
public interface IExecutionScopeFactory
{
    /// <summary>
    /// Creates and returns an <see cref="IExecutionScope"/> initialized with the
    /// provided <paramref name="context"/>. The returned scope must be disposed with
    /// <see langword="await using"/> by the caller.
    /// </summary>
    /// <param name="context">The execution context that describes the tenant, correlation,
    /// and any other ambient values to hydrate for this scope.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that resolves to the initialized
    /// <see cref="IExecutionScope"/>.
    /// </returns>
    ValueTask<IExecutionScope> CreateScopeAsync(
        IExecutionContext context, CancellationToken ct = default);
}
