namespace MicroKit.Auth.UnitTests;

public sealed class ClaimsMapperTests
{
    private readonly ClaimsMapper _sut = new();

    private static ClaimsPrincipal BuildPrincipal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims));

    [Fact]
    public void MapFromClaims_WhenAllClaimsPresent_ReturnsCurrentUser()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var principal = BuildPrincipal(
            new Claim("sub", userId.ToString()),
            new Claim("email", "user@example.com"),
            new Claim("tenant_id", tenantId.ToString()));

        var result = _sut.MapFromClaims(principal);

        result.IsSuccess.ShouldBeTrue();
        result.Value.UserId.ShouldBe(userId);
        result.Value.Email.ShouldBe("user@example.com");
        result.Value.TenantId.ShouldBe(tenantId);
        result.Value.IsAuthenticated.ShouldBeTrue();
    }

    [Fact]
    public void MapFromClaims_WhenSubMissing_ReturnsClaimsMappingError()
    {
        var principal = BuildPrincipal(new Claim("email", "user@example.com"));

        var result = _sut.MapFromClaims(principal);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<ClaimsMappingError>();
        ((ClaimsMappingError)result.Error).MissingClaim.ShouldBe("sub");
    }

    [Fact]
    public void MapFromClaims_WhenSubIsNotValidGuid_ReturnsClaimsMappingError()
    {
        var principal = BuildPrincipal(new Claim("sub", "not-a-guid"));

        var result = _sut.MapFromClaims(principal);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<ClaimsMappingError>();
    }

    [Fact]
    public void MapFromClaims_WhenEmailMissing_ReturnsCurrentUserWithNullEmail()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(new Claim("sub", userId.ToString()));

        var result = _sut.MapFromClaims(principal);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Email.ShouldBeNull();
    }

    [Fact]
    public void MapFromClaims_WhenTenantIdPresentViaTenantIdClaim_MapsTenantId()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var principal = BuildPrincipal(
            new Claim("sub", userId.ToString()),
            new Claim("tenant_id", tenantId.ToString()));

        var result = _sut.MapFromClaims(principal);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TenantId.ShouldBe(tenantId);
    }

    [Fact]
    public void MapFromClaims_WhenTenantIdPresentViaTidClaim_MapsTenantId()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var principal = BuildPrincipal(
            new Claim("sub", userId.ToString()),
            new Claim("tid", tenantId.ToString()));

        var result = _sut.MapFromClaims(principal);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TenantId.ShouldBe(tenantId);
    }

    [Fact]
    public void MapFromClaims_WhenRoleClaimsPresent_MapsRoles()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(
            new Claim("sub", userId.ToString()),
            new Claim("role", "admin"),
            new Claim("role", "auditor"));

        var result = _sut.MapFromClaims(principal);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Roles.Count.ShouldBe(2);
        result.Value.Roles.ShouldContain(r => r.Name == "admin");
        result.Value.Roles.ShouldContain(r => r.Name == "auditor");
    }

    [Fact]
    public void MapToClaims_RoundtripsSubAndEmail()
    {
        var userId = Guid.NewGuid();
        var user = FakeCurrentUserBuilder.Create()
            .WithUserId(userId)
            .WithEmail("test@example.com")
            .Build();

        var claims = _sut.MapToClaims(user).ToList();

        claims.ShouldContain(c => c.Type == "sub" && c.Value == userId.ToString());
        claims.ShouldContain(c => c.Type == "email" && c.Value == "test@example.com");
    }

    [Fact]
    public void MapToClaims_WhenTenantIdPresent_IncludesTenantIdClaim()
    {
        var tenantId = Guid.NewGuid();
        var user = FakeCurrentUserBuilder.Create().WithTenantId(tenantId).Build();

        var claims = _sut.MapToClaims(user).ToList();

        claims.ShouldContain(c => c.Type == "tenant_id" && c.Value == tenantId.ToString());
    }

    [Fact]
    public void MapToClaims_WhenRolesPresent_IncludesRoleClaims()
    {
        var user = FakeCurrentUserBuilder.Create()
            .WithRole(Role.Of("admin"))
            .WithRole(Role.Of("auditor"))
            .Build();

        var claims = _sut.MapToClaims(user).ToList();

        claims.ShouldContain(c => c.Type == "role" && c.Value == "admin");
        claims.ShouldContain(c => c.Type == "role" && c.Value == "auditor");
    }
}
