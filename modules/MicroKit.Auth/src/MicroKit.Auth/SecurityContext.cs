using AuthCurrentUser = MicroKit.Auth.CurrentUser;

namespace MicroKit.Auth;

/// <summary>
/// Scoped implementation of <see cref="ISecurityContext"/>. Reads the ambient user from
/// <see cref="ICurrentUserAccessor"/> and falls back to <see cref="CurrentUser.Anonymous"/>
/// when no user has been established.
/// </summary>
/// <param name="accessor">The accessor providing the current user for this scope.</param>
public sealed class SecurityContext(ICurrentUserAccessor accessor) : ISecurityContext
{
    /// <inheritdoc />
    /// <remarks>
    /// Returns <see cref="CurrentUser.Anonymous"/> when the accessor has no user set.
    /// The alias <c>AuthCurrentUser</c> disambiguates the class from this property's name.
    /// </remarks>
    public ICurrentUser CurrentUser => accessor.Get() ?? AuthCurrentUser.Anonymous;

    /// <inheritdoc />
    public bool IsAuthenticated => accessor.Get()?.IsAuthenticated == true;

    /// <inheritdoc />
    public bool HasTenant => accessor.Get()?.TenantId.HasValue == true;
}
