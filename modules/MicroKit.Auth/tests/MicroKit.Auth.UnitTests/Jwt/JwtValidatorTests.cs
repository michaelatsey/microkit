using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MicroKit.Auth.UnitTests.Jwt;

public sealed class JwtValidatorTests
{
    private const string ValidIssuer = "test-issuer";
    private const string ValidAudience = "test-audience";
    private const string ValidSecret = "test-secret-that-is-at-least-32-chars-long!!";

    private readonly JwtOptions _defaultOptions = new()
    {
        Issuer = ValidIssuer,
        Audience = ValidAudience,
        Secret = ValidSecret,
        Expiry = TimeSpan.FromHours(1),
        ClockSkew = TimeSpan.FromMinutes(5)
    };

    private JwtValidator BuildSut() => new(_defaultOptions);

    private static string BuildToken(
        string issuer = ValidIssuer,
        string audience = ValidAudience,
        string secret = ValidSecret,
        DateTime? expires = null,
        string? sub = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var handler = new JsonWebTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new System.Security.Claims.ClaimsIdentity([
                new System.Security.Claims.Claim("sub", sub ?? Guid.NewGuid().ToString())
            ]),
            NotBefore = DateTime.UtcNow,
            Expires = expires ?? DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        };
        return handler.CreateToken(descriptor);
    }

    [Fact]
    public async Task ValidateAsync_WhenTokenValid_ReturnsPrincipal()
    {
        var sut = BuildSut();
        var token = BuildToken();

        var result = await sut.ValidateAsync(token);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotBeNull();
    }

    [Fact]
    public async Task ValidateAsync_WhenTokenValid_PrincipalContainsSubClaim()
    {
        var sut = BuildSut();
        var userId = Guid.NewGuid().ToString();
        var token = BuildToken(sub: userId);

        var result = await sut.ValidateAsync(token);

        result.IsSuccess.ShouldBeTrue();
        result.Value.FindFirst("sub")?.Value.ShouldBe(userId);
    }

    [Fact]
    public async Task ValidateAsync_WhenTokenExpired_ReturnsFailure()
    {
        var sut = BuildSut();
        var token = BuildToken(expires: DateTime.UtcNow.AddHours(-2));

        var result = await sut.ValidateAsync(token);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<InvalidTokenError>();
    }

    [Fact]
    public async Task ValidateAsync_WhenTokenMalformed_ReturnsFailure()
    {
        var sut = BuildSut();

        var result = await sut.ValidateAsync("this.is.not.a.jwt");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<InvalidTokenError>();
    }

    [Fact]
    public async Task ValidateAsync_WhenSignatureInvalid_ReturnsFailure()
    {
        var sut = BuildSut();
        var token = BuildToken(secret: "a-completely-different-secret-at-least-32bytes!!");

        var result = await sut.ValidateAsync(token);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<InvalidTokenError>();
    }

    [Fact]
    public async Task ValidateAsync_WhenAudienceMismatch_ReturnsFailure()
    {
        var sut = BuildSut();
        var token = BuildToken(audience: "wrong-audience");

        var result = await sut.ValidateAsync(token);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<InvalidTokenError>();
    }

    [Fact]
    public async Task ValidateAsync_WhenIssuerMismatch_ReturnsFailure()
    {
        var sut = BuildSut();
        var token = BuildToken(issuer: "wrong-issuer");

        var result = await sut.ValidateAsync(token);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<InvalidTokenError>();
    }

    [Fact]
    public async Task ValidateAsync_WhenTokenIsNullOrWhiteSpace_ReturnsFailure()
    {
        var sut = BuildSut();

        var result = await sut.ValidateAsync("   ");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<InvalidTokenError>();
    }

    [Fact]
    public async Task ValidateAsync_WhenTokenIsEmpty_ReturnsFailure()
    {
        var sut = BuildSut();

        var result = await sut.ValidateAsync(string.Empty);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<InvalidTokenError>();
    }

    [Fact]
    public async Task ValidateAsync_WhenInvalidToken_ErrorContainsSafeFragment()
    {
        var sut = BuildSut();

        var result = await sut.ValidateAsync("ABCDEFGH-rest-of-malformed-token");

        result.IsFailure.ShouldBeTrue();
        var error = result.Error.ShouldBeOfType<InvalidTokenError>();
        error.TokenFragment.ShouldBe("ABCDEFGH");
    }

    [Fact]
    public async Task ValidateAsync_WhenTokenValid_NeverThrows()
    {
        var sut = BuildSut();
        var token = BuildToken();

        await Should.NotThrowAsync(async () => await sut.ValidateAsync(token));
    }
}
