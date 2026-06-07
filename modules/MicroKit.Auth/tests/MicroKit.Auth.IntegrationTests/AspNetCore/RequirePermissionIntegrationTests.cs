using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;

namespace MicroKit.Auth.IntegrationTests.AspNetCore;

public sealed class RequirePermissionIntegrationTests
{
    private static readonly Permission ReadDocs = Permission.Of("docs", "read");
    private const string ProtectedPath = "/protected";
    private const string TestScheme = "Test";

    // ── Host factory ──────────────────────────────────────────────────────────

    private static async Task<IHost> CreateHostAsync(FakePermissionChecker checker)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddAuthentication(defaultScheme: TestScheme)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestScheme, _ => { });

                    services.AddMicroKitAuth();

                    // Replace IPermissionChecker with the configured fake
                    services.AddScoped<IPermissionChecker>(_ => checker);

                    services.AddRouting();
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseMicroKitAuth();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                        endpoints.MapGet(
                            ProtectedPath,
                            [RequirePermission("docs", "read")] () => Results.Ok()));
                });
            })
            .StartAsync();

        return host;
    }

    // ── Test auth handler ─────────────────────────────────────────────────────

    // Authenticates when Authorization header is present; returns NoResult otherwise.
    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("Authorization"))
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new[]
            {
                new Claim("sub", Guid.NewGuid().ToString()),
                new Claim("email", "test@example.com"),
            };
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_WhenUserHasRequiredPermission_Returns200()
    {
        var checker = new FakePermissionChecker().Allow(ReadDocs);
        using var host = await CreateHostAsync(checker);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

        var response = await client.GetAsync(ProtectedPath);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_WhenUserLacksRequiredPermission_Returns403()
    {
        // FakePermissionChecker with no allowed permissions — denies everything
        var checker = new FakePermissionChecker();
        using var host = await CreateHostAsync(checker);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

        var response = await client.GetAsync(ProtectedPath);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_WhenUnauthenticated_Returns401()
    {
        var checker = new FakePermissionChecker();
        using var host = await CreateHostAsync(checker);
        var client = host.GetTestClient();
        // No Authorization header → TestAuthHandler returns NoResult → 401

        var response = await client.GetAsync(ProtectedPath);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
