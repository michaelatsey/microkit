namespace MicroKit.Auth.Supabase;

/// <summary>
/// Maps Supabase JWT claims to <see cref="ICurrentUser"/>.
/// </summary>
/// <remarks>
/// <para>
/// Supabase JWT structure:
/// <code>
/// {
///   "sub": "uuid",
///   "email": "user@example.com",
///   "aud": "authenticated",
///   "iss": "https://xyz.supabase.co/auth/v1",
///   "app_metadata": { "roles": ["admin"], "tenant_id": "uuid" }
/// }
/// </code>
/// </para>
/// <para>
/// <c>app_metadata</c> is parsed with <c>System.Text.Json</c> for zero-allocation JSON access.
/// <c>roles</c> and <c>tenant_id</c> are read from this nested JSON object.
/// </para>
/// <para>
/// This class is stateless and thread-safe — safe to register as a singleton.
/// </para>
/// </remarks>
public sealed class SupabaseClaimsMapper : IClaimsMapper
{
    private const string AppMetadataClaim = "app_metadata";
    private const string RolesKey = "roles";
    private const string TenantIdKey = "tenant_id";

    /// <inheritdoc />
    public Result<ICurrentUser> MapFromClaims(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue("sub")
                  ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(sub))
            return Failure<ICurrentUser>(new ClaimsMappingError("The 'sub' claim is missing or empty."));

        if (!Guid.TryParse(sub, out var userId))
            return Failure<ICurrentUser>(new ClaimsMappingError($"The 'sub' claim '{sub}' is not a valid GUID."));

        var email = principal.FindFirstValue("email")
                    ?? principal.FindFirstValue(ClaimTypes.Email);

        var (roles, tenantId) = ParseAppMetadata(principal);

        var remainingClaims = principal.Claims
            .Where(c => c.Type is not ("sub" or "email" or AppMetadataClaim)
                        && c.Type != ClaimTypes.NameIdentifier
                        && c.Type != ClaimTypes.Email)
            .GroupBy(c => c.Type)
            .ToDictionary(g => g.Key, g => g.Last().Value);

        return Success<ICurrentUser>(CurrentUser.FromClaims(
            userId,
            email,
            tenantId,
            roles,
            remainingClaims));
    }

    /// <inheritdoc />
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "app_metadata is serialized from a Dictionary<string, object?> with known value types (string[], string). Safe in library context.")]
    public IEnumerable<Claim> MapToClaims(ICurrentUser user)
    {
        yield return new Claim("sub", user.UserId.ToString());

        if (user.Email is not null)
            yield return new Claim("email", user.Email);

        if (user.Roles.Count > 0 || user.TenantId.HasValue)
        {
            var appMeta = new Dictionary<string, object?>();

            if (user.Roles.Count > 0)
                appMeta[RolesKey] = user.Roles.Select(r => r.Name).ToArray();

            if (user.TenantId.HasValue)
                appMeta[TenantIdKey] = user.TenantId.Value.ToString();

            yield return new Claim(AppMetadataClaim, JsonSerializer.Serialize(appMeta));
        }

        foreach (var (type, value) in user.Claims)
            yield return new Claim(type, value);
    }

    private static (IReadOnlyList<Role> Roles, Guid? TenantId) ParseAppMetadata(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(AppMetadataClaim);
        if (string.IsNullOrWhiteSpace(raw))
            return (Array.Empty<Role>(), null);

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            var roles = ParseRoles(root);
            var tenantId = ParseTenantId(root);

            return (roles, tenantId);
        }
        catch (JsonException)
        {
            return (Array.Empty<Role>(), null);
        }
    }

    private static IReadOnlyList<Role> ParseRoles(JsonElement root)
    {
        if (!root.TryGetProperty(RolesKey, out var rolesElement)
            || rolesElement.ValueKind != JsonValueKind.Array)
            return Array.Empty<Role>();

        var roles = new List<Role>();
        foreach (var item in rolesElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var name = item.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                    roles.Add(Role.Of(name));
            }
        }

        return roles.Count > 0 ? roles : Array.Empty<Role>();
    }

    private static Guid? ParseTenantId(JsonElement root)
    {
        if (!root.TryGetProperty(TenantIdKey, out var tidElement))
            return null;

        return tidElement.ValueKind == JsonValueKind.String
               && Guid.TryParse(tidElement.GetString(), out var guid)
            ? guid
            : null;
    }
}
