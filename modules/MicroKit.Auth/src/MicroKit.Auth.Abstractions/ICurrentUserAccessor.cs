namespace MicroKit.Auth;

/// <summary>
/// Provides ambient get/set access to the <see cref="ICurrentUser"/> for the current
/// execution context. Backed by <c>AsyncLocal&lt;ICurrentUser?&gt;</c> in the Core
/// implementation, making it host-agnostic.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifetime constraint — NEVER register as Singleton.</b> This accessor is
/// scoped to the current async execution context. Injecting it into a singleton
/// would cause the user context to bleed across requests.
/// </para>
/// <para>
/// Typically consumed by authentication middleware to set the user after token
/// validation, and by <see cref="ISecurityContext"/> to read it.
/// Application code should prefer <see cref="ISecurityContext"/> over direct use
/// of this accessor.
/// </para>
/// </remarks>
public interface ICurrentUserAccessor
{
    /// <summary>
    /// Retrieves the current user for this execution context.
    /// </summary>
    /// <returns>
    /// The <see cref="ICurrentUser"/> set for this scope, or <see langword="null"/>
    /// if no user has been established.
    /// </returns>
    ICurrentUser? Get();

    /// <summary>
    /// Sets the current user for this execution context.
    /// Overwrites any previously set value.
    /// </summary>
    /// <param name="user">The user to associate with the current scope. Must not be <see langword="null"/>.</param>
    void Set(ICurrentUser user);

    /// <summary>
    /// Clears the current user for this execution context.
    /// After calling this method, <see cref="Get"/> returns <see langword="null"/>.
    /// </summary>
    void Clear();

    /// <summary>
    /// Sets <paramref name="user"/> as the current user for the duration of the returned scope
    /// and restores the previous user when the scope is disposed.
    /// </summary>
    /// <param name="user">The user to associate with this scope.</param>
    /// <returns>
    /// An <see cref="IDisposable"/> that, when disposed, restores the user that was active
    /// before this scope was created. Nested scopes restore correctly — disposing the inner
    /// scope always restores to the outer user, never to <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// Use this method instead of a raw <see cref="Set"/> call when the user context must be
    /// established for background work (<c>Task.Run</c>, <c>Parallel.ForEachAsync</c>) or
    /// for nested impersonation scenarios where the original user must be restored.
    /// </remarks>
    IDisposable CreateScope(ICurrentUser user);
}
