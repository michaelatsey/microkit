namespace MicroKit.Auth.UnitTests.Jwt;

public sealed class JwtTokenGeneratorTests
{
    private const string TestIssuer = "test-issuer";
    private const string TestAudience = "test-audience";
    private const string TestSecret = "test-secret-that-is-at-least-32-chars-long!!";

    private readonly JwtOptions _options = new()
    {
        Issuer = TestIssuer,
        Audience = TestAudience,
        Secret = TestSecret,
        Expiry = TimeSpan.FromHours(1),
        ClockSkew = TimeSpan.FromMinutes(5)
    };

    private readonly IClaimsMapper _claimsMapper = new ClaimsMapper();

    private JwtTokenGenerator BuildSut() => new(_options, _claimsMapper);
    private JwtValidator BuildValidator() => new(_options);

    [Fact]
    public async Task GenerateAsync_WhenUserValid_ReturnsNonEmptyToken()
    {
        var sut = BuildSut();
        var user = FakeCurrentUserBuilder.Create().Build();

        var result = await sut.GenerateAsync(user);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GenerateAsync_WhenGenerated_TokenIsValidatableByJwtValidator()
    {
        var sut = BuildSut();
        var validator = BuildValidator();
        var user = FakeCurrentUserBuilder.Create().Build();

        var generateResult = await sut.GenerateAsync(user);
        generateResult.IsSuccess.ShouldBeTrue();

        var validateResult = await validator.ValidateAsync(generateResult.Value);
        validateResult.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task GenerateAsync_WhenGenerated_PrincipalContainsSubClaim()
    {
        var sut = BuildSut();
        var validator = BuildValidator();
        var userId = Guid.NewGuid();
        var user = FakeCurrentUserBuilder.Create().WithUserId(userId).Build();

        var generateResult = await sut.GenerateAsync(user);
        var validateResult = await validator.ValidateAsync(generateResult.Value);

        validateResult.IsSuccess.ShouldBeTrue();
        validateResult.Value.FindFirst("sub")?.Value.ShouldBe(userId.ToString());
    }

    [Fact]
    public async Task GenerateAsync_WhenUserHasEmail_TokenContainsEmailClaim()
    {
        var sut = BuildSut();
        var validator = BuildValidator();
        var user = FakeCurrentUserBuilder.Create().WithEmail("user@example.com").Build();

        var generateResult = await sut.GenerateAsync(user);
        var validateResult = await validator.ValidateAsync(generateResult.Value);

        validateResult.IsSuccess.ShouldBeTrue();
        validateResult.Value.FindFirst("email")?.Value.ShouldBe("user@example.com");
    }

    [Fact]
    public async Task GenerateAsync_WhenUserHasTenantId_TokenContainsTenantIdClaim()
    {
        var sut = BuildSut();
        var validator = BuildValidator();
        var tenantId = Guid.NewGuid();
        var user = FakeCurrentUserBuilder.Create().WithTenantId(tenantId).Build();

        var generateResult = await sut.GenerateAsync(user);
        var validateResult = await validator.ValidateAsync(generateResult.Value);

        validateResult.IsSuccess.ShouldBeTrue();
        validateResult.Value.FindFirst("tenant_id")?.Value.ShouldBe(tenantId.ToString());
    }

    [Fact]
    public async Task GenerateAsync_WhenUserHasRole_TokenContainsRoleClaim()
    {
        var sut = BuildSut();
        var validator = BuildValidator();
        var user = FakeCurrentUserBuilder.Create().WithRole(Role.Of("admin")).Build();

        var generateResult = await sut.GenerateAsync(user);
        var validateResult = await validator.ValidateAsync(generateResult.Value);

        validateResult.IsSuccess.ShouldBeTrue();
        validateResult.Value.FindFirst("role")?.Value.ShouldBe("admin");
    }

    [Fact]
    public async Task GenerateAsync_WhenCalled_NeverThrows()
    {
        var sut = BuildSut();
        var user = FakeCurrentUserBuilder.Create().Build();

        await Should.NotThrowAsync(async () => await sut.GenerateAsync(user));
    }

    [Fact]
    public async Task GenerateAsync_WhenUsersAreDifferent_ProducesDifferentTokens()
    {
        var sut = BuildSut();
        var user1 = FakeCurrentUserBuilder.Create().WithUserId(Guid.NewGuid()).Build();
        var user2 = FakeCurrentUserBuilder.Create().WithUserId(Guid.NewGuid()).Build();

        var first = await sut.GenerateAsync(user1);
        var second = await sut.GenerateAsync(user2);

        first.IsSuccess.ShouldBeTrue();
        second.IsSuccess.ShouldBeTrue();
        first.Value.ShouldNotBe(second.Value);
    }
}
