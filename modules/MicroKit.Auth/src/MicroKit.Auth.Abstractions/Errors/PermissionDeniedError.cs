namespace MicroKit.Auth.Errors;

/// <summary>
/// Raised when an authenticated user attempts an operation they do not have permission for.
/// Carries the specific <see cref="CheckedPermission"/> evaluated and the optional
/// <see cref="TenantId"/> scope in which it was evaluated.
/// </summary>
/// <param name="CheckedPermission">
/// The permission that was evaluated and denied. <see langword="null"/> when
/// the denial is not permission-specific (e.g. a role-level denial).
/// </param>
/// <param name="TenantId">
/// The tenant scope under which the check was performed.
/// <see langword="null"/> for system-level (non-tenant-scoped) checks.
/// </param>
public sealed record PermissionDeniedError(Permission? CheckedPermission, Guid? TenantId)
    : AuthError(
        ErrorCode.From("AUTH.PERMISSION.DENIED"),
        CheckedPermission is not null
            ? $"Access denied: permission '{CheckedPermission}' is not granted."
            : "Access denied.")
{
    /// <inheritdoc/>
    public override ErrorCategory Category => ErrorCategory.Forbidden;
}
