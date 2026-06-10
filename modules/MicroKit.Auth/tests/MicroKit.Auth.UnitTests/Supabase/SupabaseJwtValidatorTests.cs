using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using MicroKit.Auth.Supabase;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MicroKit.Auth.UnitTests.Supabase;

public sealed class SupabaseJwtValidatorTests : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly SupabaseAuthOptions _options = new()
    {
        ProjectUrl = "https://test.supabase.co",
        Issuer = "https://test.supabase.co/auth/v1",
        Audience = "authenticated",
        JwksCacheDuration = TimeSpan.FromMinutes(60),
        KeyRotationCooldown = TimeSpan.FromMinutes(5),
    };

    public void Dispose() => _key.Dispose();

    private string BuildValidToken(
        string? issuer = null,
        string? audience = null,
        DateTime? expires = null)
    {
        var handler = new JsonWebTokenHandler();
        var securityKey = new ECDsaSecurityKey(_key);
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer ?? _options.Issuer,
            Audience = audience ?? _options.Audience,
            Expires = expires ?? DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                securityKey, SecurityAlgorithms.EcdsaSha256),
        });
    }

    private string BuildJwksJson()
    {
        var jwk = JsonWebKeyConverter.ConvertFromECDsaSecurityKey(new ECDsaSecurityKey(_key));
        // Manually construct JWKS JSON since JsonWebKeySet has no SerializeToJson in v8.9
        return $@"{{""keys"":[{{""kty"":""{jwk.Kty}"",""crv"":""{jwk.Crv}"",""x"":""{jwk.X}"",""y"":""{jwk.Y}""}}]}}";
    }

    private SupabaseJwtValidator BuildSut(string? jwksJson = null)
    {
        var content = jwksJson ?? BuildJwksJson();
        var handler = new FakeHttpMessageHandler(content);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(nameof(SupabaseJwtValidator))
               .Returns(new HttpClient(handler));
        return new SupabaseJwtValidator(_options, factory);
    }

    [Fact]
    public async Task ValidateAsync_WhenTokenValid_ReturnsPrincipal()
    {
        var sut = BuildSut();
        var token = BuildValidToken();

        var result = await sut.ValidateAsync(token);

        result.IsSuccess.ShouldBeTrue();
        result.Value.FindFirstValue("iss").ShouldBe(_options.Issuer);
    }

    [Fact]
    public async Task ValidateAsync_WhenTokenExpired_ReturnsFailure()
    {
        var sut = BuildSut();
        var token = BuildValidToken(expires: DateTime.UtcNow.AddHours(-2));

        var result = await sut.ValidateAsync(token);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<InvalidTokenError>();
    }

    [Fact]
    public async Task ValidateAsync_WhenTokenMalformed_ReturnsFailure()
    {
        var sut = BuildSut();

        var result = await sut.ValidateAsync("not.a.jwt");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<InvalidTokenError>();
    }

    [Fact]
    public async Task ValidateAsync_WhenSignatureInvalid_ReturnsFailure()
    {
        using var wrongKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var wrongJwk = JsonWebKeyConverter.ConvertFromECDsaSecurityKey(new ECDsaSecurityKey(wrongKey));
        var wrongJwksJson = $@"{{""keys"":[{{""kty"":""{wrongJwk.Kty}"",""crv"":""{wrongJwk.Crv}"",""x"":""{wrongJwk.X}"",""y"":""{wrongJwk.Y}""}}]}}";
        var sut = BuildSut(wrongJwksJson);
        var token = BuildValidToken();

        var result = await sut.ValidateAsync(token);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<InvalidTokenError>();
    }

    [Fact]
    public async Task ValidateAsync_WhenAudienceMismatch_ReturnsFailure()
    {
        var sut = BuildSut();
        var token = BuildValidToken(audience: "wrong-audience");

        var result = await sut.ValidateAsync(token);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<InvalidTokenError>();
    }

    [Fact]
    public async Task ValidateAsync_WhenIssuerMismatch_ReturnsFailure()
    {
        var sut = BuildSut();
        var token = BuildValidToken(issuer: "https://wrong-issuer.supabase.co/auth/v1");

        var result = await sut.ValidateAsync(token);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<InvalidTokenError>();
    }

    [Fact]
    public async Task ValidateAsync_WhenTokenEmpty_ReturnsFailure()
    {
        var sut = BuildSut();

        var result = await sut.ValidateAsync(string.Empty);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<InvalidTokenError>();
    }

    [Fact]
    public async Task ValidateAsync_WhenCancelled_RethrowsOperationCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Use a slow handler so cancellation fires during the JWKS fetch
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>())
               .Returns(new HttpClient(new CancellingHttpMessageHandler()));
        var sut = new SupabaseJwtValidator(_options, factory);
        var token = BuildValidToken();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await sut.ValidateAsync(token, cts.Token));
    }

    private sealed class FakeHttpMessageHandler(string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            });
        }
    }

    private sealed class CancellingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromCanceled<HttpResponseMessage>(ct);
        }
    }
}
