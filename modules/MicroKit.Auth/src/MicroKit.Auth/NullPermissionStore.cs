namespace MicroKit.Auth;

/// <summary>
/// No-op <see cref="IPermissionStore"/> registered by default via <see cref="ServiceCollectionExtensions.AddMicroKitAuthCore"/>.
/// Always returns an empty permission list — effectively denying all non-SuperAdmin checks.
/// Replace by registering your own <see cref="IPermissionStore"/> after calling
/// <see cref="ServiceCollectionExtensions.AddMicroKitAuthCore"/>.
/// </summary>
internal sealed class NullPermissionStore : IPermissionStore
{
    private static readonly IReadOnlyList<Permission> Empty = Array.Empty<Permission>();

    /// <inheritdoc />
    public ValueTask<Result<IReadOnlyList<Permission>>> GetPermissionsAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken ct = default)
        => new(Success(Empty));

    /// <inheritdoc />
    public ValueTask<Result<IReadOnlyList<Permission>>> GetPermissionsAsync(
        Guid userId,
        CancellationToken ct = default)
        => new(Success(Empty));
}
