namespace MicroKit.Tenancy.AspNetCore;

using Microsoft.AspNetCore.Http;

/// <summary>
/// Resolves the current tenant from a subdomain segment, expected to be a <see cref="Guid"/>.
/// The segment index is configurable via <see cref="AspNetCoreMultitenancyOptions.SubdomainSegmentIndex"/>
/// (default: <c>0</c>). Example: <c>{guid}.app.example.com</c> at index 0 yields the GUID.
/// </summary>
/// <remarks>
/// Registered only when <see cref="AspNetCoreMultitenancyOptions.EnableSubdomain"/> is <see langword="true"/>.
/// Phase 1 limitation: only Guid-formatted subdomain segments are supported.
/// Slug-based resolution (e.g., <c>acme.example.com</c>) is deferred to Phase 2.
/// </remarks>
public sealed class SubdomainTenantResolutionStrategy(
    IHttpContextAccessor httpContextAccessor,
    IOptions<AspNetCoreMultitenancyOptions> options) : ITenantResolutionStrategy
{
    /// <inheritdoc/>
    public int Order => 30;

    /// <inheritdoc/>
    public ValueTask<Result<TenantId>> TryResolveAsync(CancellationToken ct = default)
    {
        var host = httpContextAccessor.HttpContext?.Request.Host.Host;

        if (string.IsNullOrEmpty(host))
            return ValueTask.FromResult(Failure<TenantId>(MultitenancyErrors.TenantNotFound));

        var segments = host.Split('.');
        var idx = options.Value.SubdomainSegmentIndex;

        if (idx >= segments.Length)
            return ValueTask.FromResult(Failure<TenantId>(MultitenancyErrors.TenantNotFound));

        if (!Guid.TryParse(segments[idx], out var id))
            return ValueTask.FromResult(Failure<TenantId>(MultitenancyErrors.InvalidTenantId));

        return ValueTask.FromResult(Success(new TenantId(id)));
    }
}
