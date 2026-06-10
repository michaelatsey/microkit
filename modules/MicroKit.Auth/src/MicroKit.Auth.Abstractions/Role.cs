namespace MicroKit.Auth;

/// <summary>
/// Represents a typed role assigned to a user. Equality is determined by <see cref="Name"/>
/// (record default). Roles carry no hierarchy in Abstractions; hierarchy is modelled in
/// <c>MicroKit.Auth.Roles</c>.
/// </summary>
/// <remarks>
/// Always create roles through the static factory <see cref="Of"/> or by declaring
/// compile-time constants on <c>SystemRoles</c> in <c>MicroKit.Auth.Roles</c>.
/// Never pass raw role name strings across layer boundaries.
/// <para>Convention: lowercase, e.g. <c>"admin"</c>, <c>"auditor"</c>.</para>
/// </remarks>
public sealed record Role
{
    private Role(string name) { Name = name; }

    /// <summary>
    /// The role name. Case-sensitive.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Creates a <see cref="Role"/> from a name string.
    /// This is the sole valid construction path.
    /// </summary>
    /// <param name="name">The role name. Must not be null or whitespace.</param>
    /// <returns>A new <see cref="Role"/> value object.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="name"/> is null or whitespace.
    /// </exception>
    public static Role Of(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new(name);
    }

    /// <summary>
    /// Returns the role name.
    /// </summary>
    public override string ToString() => Name;
}
