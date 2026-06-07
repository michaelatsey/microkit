namespace MicroKit.Auth;

/// <summary>
/// Concrete implementation of <see cref="ICurrentUser"/> produced by <see cref="ClaimsMapper"/>
/// after successful JWT validation. Immutable after construction.
/// </summary>
/// <remarks>
/// Do not construct directly in application or test code. Use <see cref="FromClaims"/> in
/// production code and <c>FakeCurrentUserBuilder</c> from <c>MicroKit.Auth.Testing</c> in tests.
/// </remarks>
public sealed class CurrentUser : ICurrentUser
{
    private readonly bool _isAuthenticated;

    private CurrentUser(bool isAuthenticated)
    {
        _isAuthenticated = isAuthenticated;
    }

    /// <summary>
    /// Represents an unauthenticated user. Returned by <see cref="SecurityContext"/> when no
    /// user has been established in the current execution context.
    /// </summary>
    public static readonly CurrentUser Anonymous = new(isAuthenticated: false);

    /// <summary>
    /// Creates an authenticated <see cref="CurrentUser"/> from validated claims data.
    /// </summary>
    /// <param name="userId">The user identifier mapped from the <c>sub</c> JWT claim.</param>
    /// <param name="email">The user's email address, or <see langword="null"/> if not present in the token.</param>
    /// <param name="tenantId">The tenant the user is operating under, or <see langword="null"/> for single-tenant contexts.</param>
    /// <param name="roles">The roles assigned to this user. Pass an empty list when none are present.</param>
    /// <param name="claims">Additional claims from the token. Pass an empty dictionary when none are present.</param>
    /// <returns>A new, authenticated <see cref="CurrentUser"/> instance.</returns>
    public static CurrentUser FromClaims(
        Guid userId,
        string? email,
        Guid? tenantId,
        IReadOnlyList<Role> roles,
        IReadOnlyDictionary<string, string> claims)
    {
        return new CurrentUser(isAuthenticated: true)
        {
            UserId = userId,
            Email = email,
            TenantId = tenantId,
            Roles = roles,
            Claims = claims,
        };
    }

    /// <inheritdoc />
    public Guid UserId { get; private init; } = Guid.Empty;

    /// <inheritdoc />
    public Guid? TenantId { get; private init; }

    /// <inheritdoc />
    public string? Email { get; private init; }

    /// <inheritdoc />
    public IReadOnlyList<Role> Roles { get; private init; } = Array.Empty<Role>();

    /// <inheritdoc />
    public bool IsAuthenticated => _isAuthenticated;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Claims { get; private init; } =
        new Dictionary<string, string>();
}
