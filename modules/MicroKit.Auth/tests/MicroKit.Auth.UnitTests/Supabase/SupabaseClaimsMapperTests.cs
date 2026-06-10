using MicroKit.Auth.Supabase;

namespace MicroKit.Auth.UnitTests.Supabase;

public sealed class SupabaseClaimsMapperTests
{
    private readonly SupabaseClaimsMapper _sut = new();

    private static ClaimsPrincipal BuildPrincipal(params (string Type, string Value)[] claims)
    {
        var identity = new ClaimsIdentity(
            claims.Select(c => new Claim(c.Type, c.Value)),
            authenticationType: "Test");
        return new ClaimsPrincipal(identity);
    }

    private static string AppMetadata(string[]? roles = null, string? tenantId = null)
    {
        // Build the JSON directly to avoid IL2026 trimming warning in tests
        var parts = new System.Collections.Generic.List<string>();
        if (roles is not null)
        {
            var rolesJson = string.Join(",", roles.Select(r => $"\"{r}\""));
            parts.Add($"\"roles\":[{rolesJson}]");
        }
        if (tenantId is not null)
            parts.Add($"\"tenant_id\":\"{tenantId}\"");
        return "{" + string.Join(",", parts) + "}";
    }

    [Fact]
    public void MapFromClaims_WhenAllClaimsPresent_ReturnsCurrentUser()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var principal = BuildPrincipal(
            ("sub", userId.ToString()),
            ("email", "user@example.com"),
            ("app_metadata", AppMetadata(["admin"], tenantId.ToString())));

        var result = _sut.MapFromClaims(principal);

        result.IsSuccess.ShouldBeTrue();
        result.Value.UserId.ShouldBe(userId);
        result.Value.Email.ShouldBe("user@example.com");
        result.Value.TenantId.ShouldBe(tenantId);
        result.Value.Roles.Count.ShouldBe(1);
        result.Value.Roles[0].Name.ShouldBe("admin");
        result.Value.IsAuthenticated.ShouldBeTrue();
    }

    [Fact]
    public void MapFromClaims_WhenSubMissing_ReturnsFailure()
    {
        var principal = BuildPrincipal(("email", "user@example.com"));

        var result = _sut.MapFromClaims(principal);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<ClaimsMappingError>();
    }

    [Fact]
    public void MapFromClaims_WhenSubNotAGuid_ReturnsFailure()
    {
        var principal = BuildPrincipal(("sub", "not-a-guid"));

        var result = _sut.MapFromClaims(principal);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<ClaimsMappingError>();
    }

    [Fact]
    public void MapFromClaims_WhenEmailMissing_ReturnsCurrentUserWithNullEmail()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(("sub", userId.ToString()));

        var result = _sut.MapFromClaims(principal);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Email.ShouldBeNull();
    }

    [Fact]
    public void MapFromClaims_WhenAppMetadataHasRoles_MapsRoles()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(
            ("sub", userId.ToString()),
            ("app_metadata", AppMetadata(["auditor", "viewer"])));

        var result = _sut.MapFromClaims(principal);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Roles.Count.ShouldBe(2);
        result.Value.Roles.ShouldContain(r => r.Name == "auditor");
        result.Value.Roles.ShouldContain(r => r.Name == "viewer");
    }

    [Fact]
    public void MapFromClaims_WhenTenantIdPresent_MapsCorrectly()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var principal = BuildPrincipal(
            ("sub", userId.ToString()),
            ("app_metadata", AppMetadata(tenantId: tenantId.ToString())));

        var result = _sut.MapFromClaims(principal);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TenantId.ShouldBe(tenantId);
    }

    [Fact]
    public void MapFromClaims_WhenAppMetadataAbsent_ReturnsEmptyRolesAndNullTenant()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(("sub", userId.ToString()));

        var result = _sut.MapFromClaims(principal);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Roles.ShouldBeEmpty();
        result.Value.TenantId.ShouldBeNull();
    }

    [Fact]
    public void MapToClaims_WhenUserHasRolesAndTenant_EmitsAppMetadata()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var user = CurrentUser.FromClaims(
            userId,
            "user@example.com",
            tenantId,
            [Role.Of("admin")],
            new System.Collections.Generic.Dictionary<string, string>());

        var claims = _sut.MapToClaims(user).ToList();

        claims.ShouldContain(c => c.Type == "sub" && c.Value == userId.ToString());
        claims.ShouldContain(c => c.Type == "email" && c.Value == "user@example.com");
        var appMeta = claims.FirstOrDefault(c => c.Type == "app_metadata");
        appMeta.ShouldNotBeNull();
        appMeta!.Value.ShouldContain("admin");
        appMeta.Value.ShouldContain(tenantId.ToString());
    }
}
