namespace MicroKit.Auth.Testing.Fakes;

/// <summary>
/// Fluent builder for <see cref="FakeCurrentUser"/> test instances.
/// </summary>
/// <remarks>
/// <para>
/// Always prefer this builder over constructing <see cref="FakeCurrentUser"/> directly.
/// </para>
/// <para>
/// To configure which permissions are granted, pair the built user with a
/// <see cref="FakePermissionChecker"/>:
/// <code>
/// var user = FakeCurrentUserBuilder.Create().WithRole(Role.Of("auditor")).Build();
/// var checker = new FakePermissionChecker().Allow(AuditPermissions.Read);
/// </code>
/// </para>
/// </remarks>
public sealed class FakeCurrentUserBuilder
{
    private Guid _userId = Guid.NewGuid();
    private Guid? _tenantId;
    private string? _email;
    private bool _isAuthenticated = true;
    private readonly List<Role> _roles = [];
    private readonly Dictionary<string, string> _claims = [];

    private FakeCurrentUserBuilder() { }

    /// <summary>Creates a new builder with default authenticated-user settings.</summary>
    public static FakeCurrentUserBuilder Create() => new();

    /// <summary>Sets <see cref="ICurrentUser.UserId"/>.</summary>
    public FakeCurrentUserBuilder WithUserId(Guid userId)
    {
        _userId = userId;
        return this;
    }

    /// <summary>Sets <see cref="ICurrentUser.TenantId"/>.</summary>
    public FakeCurrentUserBuilder WithTenantId(Guid tenantId)
    {
        _tenantId = tenantId;
        return this;
    }

    /// <summary>Sets <see cref="ICurrentUser.Email"/>.</summary>
    public FakeCurrentUserBuilder WithEmail(string email)
    {
        _email = email;
        return this;
    }

    /// <summary>Adds a role to <see cref="ICurrentUser.Roles"/>.</summary>
    public FakeCurrentUserBuilder WithRole(Role role)
    {
        _roles.Add(role);
        return this;
    }

    /// <summary>Adds an extra claim to <see cref="ICurrentUser.Claims"/>.</summary>
    public FakeCurrentUserBuilder WithClaim(string key, string value)
    {
        _claims[key] = value;
        return this;
    }

    /// <summary>Marks the built user as unauthenticated (<see cref="ICurrentUser.IsAuthenticated"/> = false).</summary>
    public FakeCurrentUserBuilder AsUnauthenticated()
    {
        _isAuthenticated = false;
        return this;
    }

    /// <summary>Builds and returns the configured <see cref="ICurrentUser"/>.</summary>
    public ICurrentUser Build() => new FakeCurrentUser
    {
        UserId = _userId,
        TenantId = _tenantId,
        Email = _email,
        IsAuthenticated = _isAuthenticated,
        Roles = _roles.AsReadOnly(),
        Claims = _claims,
    };
}
