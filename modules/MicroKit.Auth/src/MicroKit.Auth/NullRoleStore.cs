namespace MicroKit.Auth;

/// <summary>
/// No-op <see cref="IRoleStore"/> registered by default via <see cref="ServiceCollectionExtensions.AddMicroKitAuthCore"/>.
/// Always returns an empty role list — effectively denying all non-JWT role checks.
/// Replace by registering your own <see cref="IRoleStore"/> after calling
/// <see cref="ServiceCollectionExtensions.AddMicroKitAuthCore"/>.
/// </summary>
internal sealed class NullRoleStore : IRoleStore
{
    private static readonly IReadOnlyList<Role> Empty = Array.Empty<Role>();

    /// <inheritdoc />
    public ValueTask<Result<IReadOnlyList<Role>>> GetRolesAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken ct = default)
        => new(Success(Empty));

    /// <inheritdoc />
    public ValueTask<Result<IReadOnlyList<Role>>> GetRolesAsync(
        Guid userId,
        CancellationToken ct = default)
        => new(Success(Empty));
}
