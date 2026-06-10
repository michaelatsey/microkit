namespace MicroKit.Auth;

/// <summary>
/// Represents a fine-grained authorization permission as an immutable value object.
/// A permission is identified by a <see cref="Resource"/> and an <see cref="Action"/>
/// and serialises to <c>"{resource}:{action}"</c> (e.g. <c>"audits:create"</c>).
/// </summary>
/// <remarks>
/// Always create permissions through the static factory <see cref="Of"/> or by declaring
/// compile-time constants in a typed registry class (e.g. <c>AuditPermissions</c>).
/// Never pass raw permission strings across layer boundaries.
/// <para>
/// Wildcard conventions recognised by <see cref="IPermissionChecker"/> implementations:
/// <list type="bullet">
///   <item><c>"audits:*"</c> — all actions on the <c>audits</c> resource.</item>
///   <item><c>"*:read"</c> — read access across all resources.</item>
///   <item><c>"*:*"</c> — superadmin wildcard; use with extreme caution.</item>
/// </list>
/// Wildcard matching is evaluated by the checker implementation, not by this record.
/// </para>
/// </remarks>
public sealed record Permission
{
    private Permission(string resource, string action)
    {
        Resource = resource;
        Action = action;
    }

    /// <summary>
    /// The domain resource this permission targets (e.g. <c>"audits"</c>, <c>"non-conformities"</c>).
    /// Lowercase, kebab-case.
    /// </summary>
    public string Resource { get; }

    /// <summary>
    /// The action being permitted (e.g. <c>"read"</c>, <c>"create"</c>, <c>"validate"</c>).
    /// Lowercase.
    /// </summary>
    public string Action { get; }

    /// <summary>
    /// Creates a <see cref="Permission"/> from a resource and an action.
    /// This is the sole valid construction path.
    /// </summary>
    /// <param name="resource">The target resource. Must not be null or whitespace.</param>
    /// <param name="action">The permitted action. Must not be null or whitespace.</param>
    /// <returns>A new <see cref="Permission"/> value object.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="resource"/> or <paramref name="action"/> is null or whitespace.
    /// </exception>
    public static Permission Of(string resource, string action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        return new(resource, action);
    }

    /// <summary>
    /// Returns the canonical string form of this permission: <c>"{Resource}:{Action}"</c>.
    /// </summary>
    public override string ToString() => $"{Resource}:{Action}";
}
