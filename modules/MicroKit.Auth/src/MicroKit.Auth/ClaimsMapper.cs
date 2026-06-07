using System.Collections.ObjectModel;

namespace MicroKit.Auth;

/// <summary>
/// Default <see cref="IClaimsMapper"/> for standard OIDC/JWT claim names.
/// Maps <c>sub</c> → <see cref="ICurrentUser.UserId"/>, <c>email</c> → <see cref="ICurrentUser.Email"/>,
/// <c>tenant_id</c>/<c>tid</c> → <see cref="ICurrentUser.TenantId"/>, and <c>role</c> claims →
/// <see cref="ICurrentUser.Roles"/>.
/// </summary>
/// <remarks>
/// Provider-specific packages (Supabase, Keycloak, etc.) replace this registration with their
/// own <see cref="IClaimsMapper"/> to handle custom claim structures. Register as Singleton
/// via <see cref="ServiceCollectionExtensions.AddMicroKitAuthCore"/> using
/// <c>TryAddSingleton</c> so provider packages can override.
/// </remarks>
public sealed class ClaimsMapper : IClaimsMapper
{
    private const string SubClaim = "sub";
    private const string EmailClaim = "email";
    private const string TenantIdClaim = "tenant_id";
    private const string TidClaim = "tid";
    private const string RoleClaim = "role";

    /// <inheritdoc />
    public Result<ICurrentUser> MapFromClaims(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirst(SubClaim)?.Value
               ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (sub is null || !Guid.TryParse(sub, out var userId))
            return Failure<ICurrentUser>(new ClaimsMappingError(SubClaim));

        var email = principal.FindFirst(EmailClaim)?.Value
                 ?? principal.FindFirst(ClaimTypes.Email)?.Value;

        var tenantIdStr = principal.FindFirst(TenantIdClaim)?.Value
                       ?? principal.FindFirst(TidClaim)?.Value;
        Guid? tenantId = tenantIdStr is not null && Guid.TryParse(tenantIdStr, out var tid)
            ? tid
            : null;

        var roles = BuildRoles(principal);
        var claims = BuildClaims(principal);

        ICurrentUser user = CurrentUser.FromClaims(userId, email, tenantId, roles, claims);
        return Success(user);
    }

    /// <inheritdoc />
    public IEnumerable<Claim> MapToClaims(ICurrentUser user)
    {
        yield return new Claim(SubClaim, user.UserId.ToString());

        if (user.Email is not null)
            yield return new Claim(EmailClaim, user.Email);

        if (user.TenantId.HasValue)
            yield return new Claim(TenantIdClaim, user.TenantId.Value.ToString());

        foreach (var role in user.Roles)
            yield return new Claim(RoleClaim, role.Name);

        foreach (var (key, value) in user.Claims)
            yield return new Claim(key, value);
    }

    private static ReadOnlyCollection<Role> BuildRoles(ClaimsPrincipal principal)
    {
        var roles = principal.FindAll(RoleClaim)
            .Concat(principal.FindAll(ClaimTypes.Role))
            .Select(c => new Role(c.Value))
            .DistinctBy(r => r.Name)
            .ToList();

        return roles.AsReadOnly();
    }

    private static Dictionary<string, string> BuildClaims(ClaimsPrincipal principal)
    {
        var claims = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var claim in principal.Claims)
            claims.TryAdd(claim.Type, claim.Value);
        return claims;
    }
}
