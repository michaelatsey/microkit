
// ON PREFERA L'APPROCHE  MIDDLEWARE  A ASP.NET CORE HANDLER POUR UNE MEILLEURE FLEXIBILITÉ ET MAINTENANCE,
// MAIS VOICI UN EXEMPLE D'IMPLEMENTATION D'UN HANDLER ASP.NET CORE POUR MICROKIT.SECURITY

//namespace MicroKit.Security.AspNetCore.Authentication;

//using System.Security.Claims;
//using System.Text.Encodings.Web;
//using Microsoft.AspNetCore.Authentication;
//using Microsoft.Extensions.Logging;
//using Microsoft.Extensions.Options;
//using MicroKit.Security.Abstractions.Enums;
//using MicroKit.Security.Core.Services;
//using AuthenticationScheme = Abstractions.Enums.AuthenticationScheme;

///// <summary>
///// ASP.NET Core authentication handler for MicroKit.Security.
///// </summary>
//public sealed class MicroKitAuthenticationHandler(
//    IOptionsMonitor<MicroKitAuthenticationOptions> options,
//    ILoggerFactory logger,
//    UrlEncoder encoder,
//    ISecurityService securityService,
//    IClientContextAccessor clientContextAccessor)
//    : AuthenticationHandler<MicroKitAuthenticationOptions>(options, logger, encoder)
//{
//    /// <summary>
//    /// Authentication scheme name.
//    /// </summary>
//    public const string SchemeName = "MicroKit";

//    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
//    {
//        var (credentials, scheme) = ExtractCredentials();

//        if (string.IsNullOrEmpty(credentials))
//        {
//            return AuthenticateResult.NoResult();
//        }

//        var principal = await securityService.AuthenticateAsync(
//            credentials.AsMemory(),
//            scheme,
//            Context.RequestAborted);

//        if (principal is null || !principal.IsAuthenticated)
//        {
//            return AuthenticateResult.Fail("Invalid credentials");
//        }

//        // Convert to ClaimsPrincipal
//        var claims = new List<Claim>();

//        if (principal.Identifier is not null)
//        {
//            claims.Add(new Claim(ClaimTypes.NameIdentifier, principal.Identifier));
//        }

//        if (principal.DisplayName is not null)
//        {
//            claims.Add(new Claim(ClaimTypes.Name, principal.DisplayName));
//        }

//        foreach (var claim in principal.Claims)
//        {
//            claims.Add(new Claim(claim.Type, claim.Value));
//        }

//        var identity = new ClaimsIdentity(claims, SchemeName);
//        var claimsPrincipal = new ClaimsPrincipal(identity);

//        var ticket = new AuthenticationTicket(claimsPrincipal, SchemeName);
//        return AuthenticateResult.Success(ticket);
//    }

//    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
//    {
//        Response.StatusCode = 401;
//        Response.Headers.WWWAuthenticate = Options.Challenge ?? "Bearer";
//        return Task.CompletedTask;
//    }

//    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
//    {
//        Response.StatusCode = 403;
//        return Task.CompletedTask;
//    }


//}

///// <summary>
///// Authentication options for MicroKit handler.
///// </summary>
//public sealed class MicroKitAuthenticationOptions : AuthenticationSchemeOptions
//{
//    /// <summary>
//    /// Header name for API key.
//    /// </summary>
//    public string ApiKeyHeader { get; set; } = "X-Api-Key";

//    /// <summary>
//    /// Challenge header value.
//    /// </summary>
//    public string? Challenge { get; set; } = "Bearer";
//}
