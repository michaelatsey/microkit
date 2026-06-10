namespace MicroKit.Auth;

/// <summary>
/// No-op <see cref="IRolePermissionMap"/> registered by default via <see cref="ServiceCollectionExtensions.AddMicroKitAuthCore"/>.
/// Returns an empty permission list for every role — roles grant no permissions until replaced
/// by a configured map (e.g. via <c>AddInMemoryRoles(configureMap: ...)</c> in
/// <c>MicroKit.Auth.Roles</c>).
/// </summary>
internal sealed class NullRolePermissionMap : IRolePermissionMap
{
    private static readonly IReadOnlyList<Permission> Empty = Array.Empty<Permission>();

    /// <inheritdoc />
    public IReadOnlyList<Permission> GetPermissionsForRole(Role role) => Empty;
}
