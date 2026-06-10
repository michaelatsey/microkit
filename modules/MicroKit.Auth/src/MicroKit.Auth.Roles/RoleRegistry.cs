namespace MicroKit.Auth.Roles;

/// <summary>
/// Compile-time catalog of all <see cref="Role"/> values declared by the application.
/// Populated once at startup via <see cref="Register"/>; provides a discoverable, stable snapshot
/// of the full role surface for validation, documentation, and integration tests.
/// </summary>
/// <remarks>
/// Register your roles at DI setup time using
/// <see cref="ServiceCollectionExtensions.AddRoleRegistry"/>. After the application starts,
/// <see cref="All"/> is a live view of whatever has been registered — no copy is taken.
/// <para>
/// This class is not thread-safe for concurrent <see cref="Register"/> calls. Populate the registry
/// once during the DI configuration phase (single-threaded startup) and treat it as read-only
/// thereafter.
/// </para>
/// </remarks>
public sealed class RoleRegistry
{
    private readonly HashSet<Role> _set = [];
    private readonly List<Role> _list = [];

    /// <summary>
    /// All roles currently registered. Empty before any <see cref="Register"/> call.
    /// Returns a live reference — reflects subsequent <see cref="Register"/> calls.
    /// </summary>
    public IReadOnlyList<Role> All => _list;

    /// <summary>
    /// Adds one or more roles to the catalog.
    /// Duplicate entries (by name equality) are silently ignored; the catalog never contains
    /// duplicate roles.
    /// Returns <c>this</c> for fluent chaining.
    /// </summary>
    /// <param name="roles">The roles to register. Must not be <see langword="null"/>; individual elements must not be <see langword="null"/>.</param>
    /// <returns>This <see cref="RoleRegistry"/> instance for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="roles"/> is <see langword="null"/>,
    /// or when any element in the array is <see langword="null"/>.
    /// </exception>
    public RoleRegistry Register(params Role[] roles)
    {
        ArgumentNullException.ThrowIfNull(roles);

        foreach (var role in roles)
        {
            ArgumentNullException.ThrowIfNull(role);
            if (_set.Add(role))
                _list.Add(role);
        }

        return this;
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="role"/> has been registered in the catalog.
    /// </summary>
    /// <param name="role">The role to look up.</param>
    /// <returns><see langword="true"/> when the catalog contains the role; <see langword="false"/> otherwise.</returns>
    public bool Contains(Role role)
    {
        ArgumentNullException.ThrowIfNull(role);
        return _set.Contains(role);
    }
}
