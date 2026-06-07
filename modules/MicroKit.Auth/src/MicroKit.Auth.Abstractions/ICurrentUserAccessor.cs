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
}
