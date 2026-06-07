namespace MicroKit.Auth.Testing.Fakes;

/// <summary>
/// Mutable <see cref="ICurrentUser"/> test double. Construct via
/// <see cref="FakeCurrentUserBuilder"/> rather than directly so tests stay
/// readable and future property additions do not break call sites.
/// </summary>
public sealed class FakeCurrentUser : ICurrentUser
{
    /// <inheritdoc />
    public Guid UserId { get; set; } = Guid.NewGuid();

    /// <inheritdoc />
    public Guid? TenantId { get; set; }

    /// <inheritdoc />
    public string? Email { get; set; }

    /// <inheritdoc />
    public IReadOnlyList<Role> Roles { get; set; } = Array.Empty<Role>();

    /// <inheritdoc />
    public bool IsAuthenticated { get; set; } = true;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Claims { get; set; } =
        new Dictionary<string, string>();
}
