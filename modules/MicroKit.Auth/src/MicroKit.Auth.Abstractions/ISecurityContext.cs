namespace MicroKit.Auth;

/// <summary>
/// Provides the security state for the current request or execution scope.
/// Composed of the authenticated user and convenience properties for common guards.
/// </summary>
/// <remarks>
/// Register as <b>Scoped</b> in the DI container. Never resolve from a singleton.
/// The underlying <see cref="CurrentUser"/> is set by the authentication middleware
/// after JWT validation via <see cref="ICurrentUserAccessor"/>.
/// </remarks>
public interface ISecurityContext
{
    /// <summary>
    /// Gets the currently authenticated user.
    /// When no user has been authenticated, implementations should return an anonymous
    /// representation with <see cref="ICurrentUser.IsAuthenticated"/> set to <see langword="false"/>.
    /// </summary>
    ICurrentUser CurrentUser { get; }

    /// <summary>
    /// Gets a value indicating whether a user has been authenticated in the current scope.
    /// Equivalent to <c>CurrentUser.IsAuthenticated</c>.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Gets a value indicating whether the current user is operating within a tenant context.
    /// Equivalent to <c>CurrentUser.TenantId.HasValue</c>.
    /// </summary>
    bool HasTenant { get; }
}
