namespace MicroKit.Auth;

/// <summary>
/// Represents a typed role assigned to a user. Equality is determined by <see cref="Name"/>
/// (record default). Roles carry no hierarchy in Abstractions; hierarchy is modelled in
/// <c>MicroKit.Auth.Roles</c>.
/// </summary>
/// <remarks>
/// Declare all built-in roles as static fields on <c>SystemRoles</c> in
/// <c>MicroKit.Auth.Permissions</c>. Never compare roles by raw string in domain code.
/// </remarks>
/// <param name="Name">
/// The role name. Case-sensitive. Convention: lowercase (e.g. <c>"admin"</c>, <c>"auditor"</c>).
/// </param>
public sealed record Role(string Name)
{
    /// <summary>
    /// Returns the role name.
    /// </summary>
    public override string ToString() => Name;
}
