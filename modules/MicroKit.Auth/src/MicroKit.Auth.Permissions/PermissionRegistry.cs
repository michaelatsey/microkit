namespace MicroKit.Auth.Permissions;

/// <summary>
/// Compile-time catalog of all <see cref="Permission"/> values declared by the application.
/// Populated once at startup via <see cref="Register"/>; provides a discoverable, stable snapshot
/// of the full permission surface for validation, documentation, and integration tests.
/// </summary>
/// <remarks>
/// Register your permissions at DI setup time using
/// <see cref="ServiceCollectionExtensions.AddPermissionRegistry"/>. After the application starts,
/// <see cref="All"/> is a live view of whatever has been registered — no copy is taken.
/// <para>
/// This class is not thread-safe for concurrent <see cref="Register"/> calls. Populate the registry
/// once during the DI configuration phase (single-threaded startup) and treat it as read-only
/// thereafter.
/// </para>
/// </remarks>
public sealed class PermissionRegistry
{
    private readonly HashSet<Permission> _set = [];
    private readonly List<Permission> _list = [];

    /// <summary>
    /// All permissions currently registered. Empty before any <see cref="Register"/> call.
    /// Returns a live reference — reflects subsequent <see cref="Register"/> calls.
    /// </summary>
    public IReadOnlyList<Permission> All => _list;

    /// <summary>
    /// Adds one or more permissions to the catalog.
    /// Duplicate entries (by structural value equality on <c>Resource + Action</c>) are silently
    /// ignored; the catalog never contains duplicate permissions.
    /// Returns <c>this</c> for fluent chaining.
    /// </summary>
    /// <param name="permissions">The permissions to register. Must not be <see langword="null"/>; individual elements must not be <see langword="null"/>.</param>
    /// <returns>This <see cref="PermissionRegistry"/> instance for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="permissions"/> is <see langword="null"/>,
    /// or when any element in the array is <see langword="null"/>.
    /// </exception>
    public PermissionRegistry Register(params Permission[] permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        foreach (var permission in permissions)
        {
            ArgumentNullException.ThrowIfNull(permission);
            if (_set.Add(permission))
                _list.Add(permission);
        }

        return this;
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="permission"/> has been registered in the catalog.
    /// </summary>
    /// <param name="permission">The permission to look up.</param>
    /// <returns><see langword="true"/> when the catalog contains the permission; <see langword="false"/> otherwise.</returns>
    public bool Contains(Permission permission)
    {
        ArgumentNullException.ThrowIfNull(permission);
        return _set.Contains(permission);
    }
}
