# MicroKit.Security

A comprehensive, high-performance security ecosystem for .NET 10 with full AOT/Trimming compatibility.

---

## Table of Contents

1. [Overview](#overview)
2. [Architecture](#architecture)
3. [Installation](#installation)
4. [Quick Start](#quick-start)
5. [Package Reference](#package-reference)
   - [MicroKit.Security.Abstractions](#microkitsecurityabstractions)
   - [MicroKit.Security.Core](#microkitsecuritycore)
   - [MicroKit.Security.AspNetCore](#microkitsecurityaspnetcore)
   - [MicroKit.Security.Jwt](#microkitsecurityjwt)
   - [MicroKit.Security.ApiKey](#microkitsecurityapikey)
   - [MicroKit.Security.Cognito](#microkitsecuritycognito)
   - [MicroKit.Security.AzureAd](#microkitsecurityazuread)
   - [MicroKit.Security.MultiTenancy](#microkitsecuritymultitenancy)
   - [MicroKit.Security.Messaging](#microkitsecuritymessaging)
6. [Configuration Reference](#configuration-reference)
7. [Advanced Scenarios](#advanced-scenarios)
8. [Best Practices](#best-practices)
9. [Troubleshooting](#troubleshooting)

---

## Overview

MicroKit.Security provides a unified, extensible authentication and authorization framework designed for microservices architectures. Key features include:

- **AOT/Trimming Compatible**: Full support for Native AOT and IL trimming
- **High Performance**: Zero-allocation APIs using `Span<T>`, `ValueTask`, and object pooling
- **Provider Agnostic**: Support for JWT, API Keys, AWS Cognito, Azure AD, and custom providers
- **Multi-Tenancy**: Built-in tenant resolution and isolation
- **Messaging Security**: Secure context propagation for MassTransit and Azure Service Bus

### Design Principles

| Principle | Implementation |
|-----------|----------------|
| Immutability | All models are `record` or `readonly record struct` |
| Testability | `TimeProvider` injection, interface-based design |
| Performance | `ReadOnlySpan<T>`, `ValueTask`, minimal allocations |
| Extensibility | Provider pattern, strategy pattern for resolvers |

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         Application Layer                                │
├─────────────────────────────────────────────────────────────────────────┤
│  MicroKit.Security.AspNetCore                                           │
│  ├── SecurityMiddleware                                                 │
│  ├── MicroKitAuthenticationHandler                                      │
│  └── Endpoint Extensions                                                │
├─────────────────────────────────────────────────────────────────────────┤
│  MicroKit.Security.Core                                                 │
│  ├── SecurityService (orchestrator)                                     │
│  ├── AuthenticationProviderFactory                                      │
│  ├── CachedAuthenticationProvider                                       │
│  └── ClientContextAccessor                                              │
├──────────────┬──────────────┬──────────────┬──────────────┬─────────────┤
│     Jwt      │    ApiKey    │   Cognito    │   AzureAd    │  Extensions │
│   Provider   │   Provider   │   Provider   │   Provider   │             │
├──────────────┴──────────────┴──────────────┴──────────────┼─────────────┤
│                                                           │ MultiTenant │
│                                                           │  Messaging  │
├───────────────────────────────────────────────────────────┴─────────────┤
│  MicroKit.Security.Abstractions (Zero Dependencies)                     │
│  ├── IClientContext, ISecurityPrincipal                                 │
│  ├── IApiKeyValidator                                                   │
│  └── Enums, Records, Exceptions                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Installation

### Via NuGet

```bash
# Core packages (required)
dotnet add package MicroKit.Security.Abstractions
dotnet add package MicroKit.Security.Core

# ASP.NET Core integration
dotnet add package MicroKit.Security.AspNetCore

# Authentication providers (choose as needed)
dotnet add package MicroKit.Security.Jwt
dotnet add package MicroKit.Security.ApiKey
dotnet add package MicroKit.Security.Cognito
dotnet add package MicroKit.Security.AzureAd

# Extensions
dotnet add package MicroKit.Security.MultiTenancy
dotnet add package MicroKit.Security.Messaging
```

### Package References

```xml
<ItemGroup>
  <PackageReference Include="MicroKit.Security.AspNetCore" Version="1.0.0" />
  <PackageReference Include="MicroKit.Security.Jwt" Version="1.0.0" />
  <PackageReference Include="MicroKit.Security.ApiKey" Version="1.0.0" />
</ItemGroup>
```

---

## Quick Start

### Minimal API with JWT Authentication

```csharp
using MicroKit.Security.AspNetCore.DependencyInjection;
using MicroKit.Security.Jwt.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Add MicroKit Security with JWT provider
builder.Services
    .AddMicroKitSecurity(options =>
    {
        options.DefaultScheme = AuthenticationScheme.Jwt;
        options.RequireAuthentication = true;
    })
    .AddJwtProvider(options =>
    {
        options.SecretKey = builder.Configuration["Jwt:SecretKey"]!;
        options.Issuer = "https://myapp.com";
        options.Audience = "myapp-api";
        options.TokenLifetime = TimeSpan.FromHours(1);
    });

// Add ASP.NET Core integration
builder.Services.AddMicroKitAspNetCore();

var app = builder.Build();

// Use security middleware
app.UseMicroKitSecurity();

// Protected endpoint
app.MapGet("/api/profile", (IClientContextAccessor accessor) =>
{
    var context = accessor.Context;
    return Results.Ok(new
    {
        UserId = context.Principal.Identifier,
        Name = context.Principal.DisplayName,
        TenantId = context.TenantId
    });
}).RequireAuthentication();

// Public endpoint
app.MapGet("/api/health", () => Results.Ok("Healthy"))
    .AllowAnonymous();

app.Run();
```

### Controller-based API with Multiple Providers

```csharp
using MicroKit.Security.AspNetCore.DependencyInjection;
using MicroKit.Security.Jwt.DependencyInjection;
using MicroKit.Security.ApiKey.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddMicroKitSecurity(options =>
    {
        options.DefaultScheme = AuthenticationScheme.Jwt;
        options.FallbackSchemes = [AuthenticationScheme.ApiKey];
    })
    .AddJwtProvider(options =>
    {
        options.SecretKey = builder.Configuration["Jwt:SecretKey"]!;
        options.Issuer = "https://myapp.com";
    })
    .AddApiKeyProvider(options =>
    {
        options.HeaderName = "X-API-Key";
        options.QueryParameterName = "api_key";
    });

builder.Services.AddMicroKitAspNetCore();
builder.Services.AddControllers();

var app = builder.Build();

app.UseMicroKitSecurity();
app.MapControllers();

app.Run();
```

```csharp
[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly IClientContextAccessor _contextAccessor;
    private readonly ISecurityService _securityService;

    public OrdersController(
        IClientContextAccessor contextAccessor,
        ISecurityService securityService)
    {
        _contextAccessor = contextAccessor;
        _securityService = securityService;
    }

    [HttpGet]
    [RequirePermission("orders:read")]
    public IActionResult GetOrders()
    {
        var userId = _contextAccessor.Context.Principal.Identifier;
        // Fetch orders for user...
        return Ok(orders);
    }

    [HttpPost]
    [RequirePermission("orders:write")]
    public IActionResult CreateOrder([FromBody] CreateOrderRequest request)
    {
        // Create order...
        return Created($"/api/orders/{order.Id}", order);
    }
}
```

---

## Package Reference

### MicroKit.Security.Abstractions

The foundational package containing contracts and interfaces with zero external dependencies.

#### Key Types

| Type | Description |
|------|-------------|
| `IClientContext` | Represents the authenticated client context for the current request |
| `ISecurityPrincipal` | Represents the authenticated identity |
| `SecurityClaim` | Immutable claim record struct |
| `IApiKeyValidator` | Contract for API key validation |
| `AuthenticationScheme` | Enum of supported authentication schemes |
| `ValidationStatus` | Enum of validation result statuses |

#### Usage

```csharp
// Creating a security principal
var claims = new List<SecurityClaim>
{
    new(ClaimTypes.Subject, "user-123"),
    new(ClaimTypes.Email, "user@example.com"),
    new(ClaimTypes.Role, "admin"),
    new("tenant_id", "tenant-456")
};

var principal = new SecurityPrincipal(
    Identifier: "user-123",
    DisplayName: "John Doe",
    Claims: claims);

// Creating a client context
var context = new ClientContext(
    CorrelationId: Guid.NewGuid().ToString("N"),
    Principal: principal,
    Scheme: AuthenticationScheme.Jwt,
    TenantId: "tenant-456",
    CreatedAt: TimeProvider.System.GetUtcNow());

// Using extension methods
var email = principal.GetEmail();
var roles = principal.GetRoles();
var hasAdminRole = principal.HasRole("admin");
var tenantId = principal.GetTenantId();
```

#### Claim Types

```csharp
// Standard claim types
ClaimTypes.Subject      // "sub"
ClaimTypes.Email        // "email"
ClaimTypes.Name         // "name"
ClaimTypes.GivenName    // "given_name"
ClaimTypes.FamilyName   // "family_name"
ClaimTypes.Role         // "role"
ClaimTypes.Permission   // "permission"
ClaimTypes.Scope        // "scope"
ClaimTypes.TenantId     // "tenant_id"
ClaimTypes.ClientId     // "client_id"
ClaimTypes.Issuer       // "iss"
ClaimTypes.Audience     // "aud"
ClaimTypes.Expiration   // "exp"
ClaimTypes.IssuedAt     // "iat"
ClaimTypes.NotBefore    // "nbf"
ClaimTypes.JwtId        // "jti"
```

---

### MicroKit.Security.Core

The core package containing the security service orchestrator, provider factory, and caching infrastructure.

#### Service Registration

```csharp
builder.Services.AddMicroKitSecurity(options =>
{
    // Default authentication scheme
    options.DefaultScheme = AuthenticationScheme.Jwt;
    
    // Fallback schemes tried in order if default fails
    options.FallbackSchemes = [AuthenticationScheme.ApiKey];
    
    // Require authentication for all requests
    options.RequireAuthentication = true;
    
    // Allow anonymous access to specific paths
    options.AnonymousPaths = ["/health", "/metrics", "/swagger"];
    
    // Enable caching
    options.EnableCaching = true;
    options.Cache = new CacheOptions
    {
        DefaultExpiration = TimeSpan.FromMinutes(5),
        MaxCacheSize = 10000,
        SlidingExpiration = true
    };
});
```

#### ISecurityService

```csharp
public interface ISecurityService
{
    ValueTask<AuthenticationResult> AuthenticateAsync(
        AuthenticationScheme scheme,
        string credential,
        CancellationToken cancellationToken = default);
    
    ValueTask<AuthenticationResult> AuthenticateAsync(
        string credential,
        CancellationToken cancellationToken = default);
    
    ValueTask<bool> ValidateAsync(
        ISecurityPrincipal principal,
        CancellationToken cancellationToken = default);
}
```

```csharp
// Manual authentication
public class AuthController : ControllerBase
{
    private readonly ISecurityService _securityService;
    private readonly IJwtTokenService _jwtService;

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        // Validate credentials against your user store
        var user = await _userService.ValidateCredentialsAsync(
            request.Email, request.Password);
        
        if (user is null)
            return Unauthorized();

        // Generate JWT token
        var token = await _jwtService.GenerateTokenAsync(
            new SecurityPrincipal(
                Identifier: user.Id,
                DisplayName: user.Name,
                Claims: user.Claims));

        return Ok(new { Token = token.Token, ExpiresAt = token.ExpiresAt });
    }
}
```

#### IClientContextAccessor

```csharp
public interface IClientContextAccessor
{
    IClientContext? Context { get; set; }
    bool HasContext { get; }
}
```

```csharp
// Accessing the current client context
public class OrderService
{
    private readonly IClientContextAccessor _accessor;

    public OrderService(IClientContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public async Task<Order> CreateOrderAsync(CreateOrderRequest request)
    {
        var context = _accessor.Context 
            ?? throw new InvalidOperationException("No authenticated context");

        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = context.Principal.Identifier!,
            TenantId = context.TenantId,
            CorrelationId = context.CorrelationId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Save order...
        return order;
    }
}
```

---

### MicroKit.Security.AspNetCore

ASP.NET Core integration providing middleware, authentication handler, and endpoint extensions.

#### Configuration

```csharp
builder.Services.AddMicroKitAspNetCore(options =>
{
    // Header names for token extraction
    options.AuthorizationHeader = "Authorization";
    options.ApiKeyHeader = "X-API-Key";
    options.TenantHeader = "X-Tenant-Id";
    options.CorrelationHeader = "X-Correlation-Id";
    
    // Challenge behavior
    options.SuppressWwwAuthenticateHeader = false;
    options.Realm = "MicroKit";
    
    // Error handling
    options.IncludeErrorDetails = builder.Environment.IsDevelopment();
    
    // Response customization
    options.OnAuthenticationFailed = async context =>
    {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsJsonAsync(new
        {
            Error = "Authentication failed",
            Message = context.Failure?.Message
        });
    };
});
```

#### Middleware Pipeline

```csharp
var app = builder.Build();

// Add before other middleware that requires authentication
app.UseMicroKitSecurity();

// Or use the authentication/authorization middleware separately
app.UseAuthentication();
app.UseAuthorization();
```

#### Endpoint Extensions

```csharp
// Minimal API endpoints
app.MapGet("/api/public", () => "Hello")
    .AllowAnonymous();

app.MapGet("/api/protected", () => "Secret")
    .RequireAuthentication();

app.MapGet("/api/admin", () => "Admin only")
    .RequireAuthentication()
    .RequirePermission("admin:access");

app.MapGet("/api/tenant/{tenantId}", (string tenantId) => $"Tenant: {tenantId}")
    .RequireAuthentication()
    .RequireTenant();

// Multiple permissions (AND logic)
app.MapDelete("/api/users/{id}", (string id) => Results.NoContent())
    .RequirePermissions("users:read", "users:delete");

// Role-based access
app.MapPost("/api/settings", () => Results.Ok())
    .RequireRole("administrator");
```

#### Authorization Attributes

```csharp
[ApiController]
[Route("api/[controller]")]
[RequireAuthentication] // Apply to all actions
public class UsersController : ControllerBase
{
    [HttpGet]
    [RequirePermission("users:read")]
    public IActionResult GetUsers() { }

    [HttpGet("{id}")]
    [RequirePermission("users:read")]
    public IActionResult GetUser(string id) { }

    [HttpPost]
    [RequirePermission("users:write")]
    public IActionResult CreateUser([FromBody] CreateUserRequest request) { }

    [HttpDelete("{id}")]
    [RequirePermissions("users:read", "users:delete")]
    public IActionResult DeleteUser(string id) { }

    [HttpGet("admin")]
    [RequireRole("administrator")]
    public IActionResult AdminOnly() { }
}
```

#### HttpContext Extensions

```csharp
public class MyMiddleware
{
    public async Task InvokeAsync(HttpContext context)
    {
        // Get client context
        var clientContext = context.GetClientContext();
        
        // Get security principal
        var principal = context.GetSecurityPrincipal();
        
        // Check authentication
        if (context.IsAuthenticated())
        {
            var userId = principal?.Identifier;
            var tenantId = context.GetTenantId();
            var correlationId = context.GetCorrelationId();
        }

        await _next(context);
    }
}
```

---

### MicroKit.Security.Jwt

JWT token generation and validation provider.

#### Configuration

```csharp
builder.Services.AddJwtProvider(options =>
{
    // Signing configuration
    options.SecretKey = builder.Configuration["Jwt:SecretKey"]!;
    options.Algorithm = SecurityAlgorithms.HmacSha256; // Default
    
    // Or use RSA keys
    options.RsaPrivateKeyPem = File.ReadAllText("private.pem");
    options.RsaPublicKeyPem = File.ReadAllText("public.pem");
    options.Algorithm = SecurityAlgorithms.RsaSha256;
    
    // Token parameters
    options.Issuer = "https://auth.myapp.com";
    options.Audience = "myapp-api";
    options.TokenLifetime = TimeSpan.FromHours(1);
    options.RefreshTokenLifetime = TimeSpan.FromDays(7);
    options.ClockSkew = TimeSpan.FromMinutes(5);
    
    // Validation
    options.ValidateIssuer = true;
    options.ValidateAudience = true;
    options.ValidateLifetime = true;
    options.ValidateIssuerSigningKey = true;
    
    // Additional valid issuers/audiences
    options.ValidIssuers = ["https://auth.myapp.com", "https://auth-backup.myapp.com"];
    options.ValidAudiences = ["myapp-api", "myapp-admin"];
    
    // Claim mapping
    options.NameClaimType = "name";
    options.RoleClaimType = "role";
});
```

#### Token Generation

```csharp
public class AuthService
{
    private readonly IJwtTokenService _jwtService;

    public AuthService(IJwtTokenService jwtService)
    {
        _jwtService = jwtService;
    }

    public async Task<TokenResponse> GenerateTokensAsync(User user)
    {
        var principal = new SecurityPrincipal(
            Identifier: user.Id,
            DisplayName: user.Name,
            Claims: new List<SecurityClaim>
            {
                new(ClaimTypes.Email, user.Email),
                new(ClaimTypes.Role, user.Role),
                new(ClaimTypes.TenantId, user.TenantId)
            });

        // Generate access token
        var accessToken = await _jwtService.GenerateTokenAsync(principal);

        // Generate refresh token
        var refreshToken = await _jwtService.GenerateRefreshTokenAsync(principal);

        return new TokenResponse
        {
            AccessToken = accessToken.Token,
            RefreshToken = refreshToken.Token,
            ExpiresAt = accessToken.ExpiresAt,
            TokenType = "Bearer"
        };
    }

    public async Task<TokenResponse?> RefreshTokensAsync(string refreshToken)
    {
        var result = await _jwtService.ValidateRefreshTokenAsync(refreshToken);
        
        if (!result.IsValid)
            return null;

        return await GenerateTokensAsync(result.Principal!);
    }
}
```

#### Custom Claims

```csharp
var additionalClaims = new Dictionary<string, object>
{
    ["custom_claim"] = "custom_value",
    ["permissions"] = new[] { "read", "write", "delete" },
    ["metadata"] = new { key = "value" }
};

var token = await _jwtService.GenerateTokenAsync(
    principal,
    additionalClaims: additionalClaims);
```

---

### MicroKit.Security.ApiKey

API key authentication provider with key management.

#### Configuration

```csharp
builder.Services.AddApiKeyProvider(options =>
{
    // Header/query parameter names
    options.HeaderName = "X-API-Key";
    options.QueryParameterName = "api_key";
    options.Prefix = "ApiKey "; // Optional prefix in Authorization header
    
    // Key format validation
    options.KeyLength = 32;
    options.KeyPrefix = "mk_"; // Required prefix for keys
    
    // Security
    options.HashAlgorithm = ApiKeyHashAlgorithm.SHA256;
    options.EnableRateLimiting = true;
    options.RateLimitPerMinute = 1000;
    
    // Expiration
    options.DefaultKeyLifetime = TimeSpan.FromDays(365);
    options.AllowExpiredKeyGracePeriod = TimeSpan.FromDays(7);
});

// Configure key store (choose one)

// In-memory store (for development/testing)
builder.Services.AddInMemoryApiKeyStore();

// Or implement custom store
builder.Services.AddSingleton<IApiKeyStore, DatabaseApiKeyStore>();
```

#### IApiKeyService

```csharp
public interface IApiKeyService
{
    ValueTask<ApiKeyCreateResult> CreateKeyAsync(
        CreateApiKeyRequest request,
        CancellationToken cancellationToken = default);
    
    ValueTask<bool> RevokeKeyAsync(
        string keyId,
        string? reason = null,
        CancellationToken cancellationToken = default);
    
    ValueTask<ApiKeyInfo?> GetKeyInfoAsync(
        string keyId,
        CancellationToken cancellationToken = default);
    
    ValueTask<IReadOnlyList<ApiKeyInfo>> ListKeysAsync(
        string? ownerId = null,
        CancellationToken cancellationToken = default);
    
    ValueTask<bool> RotateKeyAsync(
        string keyId,
        CancellationToken cancellationToken = default);
}
```

#### Key Management

```csharp
[ApiController]
[Route("api/keys")]
[RequirePermission("api_keys:manage")]
public class ApiKeyController : ControllerBase
{
    private readonly IApiKeyService _apiKeyService;
    private readonly IClientContextAccessor _contextAccessor;

    public ApiKeyController(
        IApiKeyService apiKeyService,
        IClientContextAccessor contextAccessor)
    {
        _apiKeyService = apiKeyService;
        _contextAccessor = contextAccessor;
    }

    [HttpPost]
    public async Task<IActionResult> CreateKey([FromBody] CreateKeyRequest request)
    {
        var ownerId = _contextAccessor.Context!.Principal.Identifier!;

        var result = await _apiKeyService.CreateKeyAsync(new CreateApiKeyRequest
        {
            Name = request.Name,
            Description = request.Description,
            OwnerId = ownerId,
            TenantId = _contextAccessor.Context.TenantId,
            Scopes = request.Scopes,
            ExpiresAt = request.ExpiresAt,
            Metadata = request.Metadata
        });

        // IMPORTANT: The plain text key is only available once!
        return Created($"/api/keys/{result.KeyId}", new
        {
            result.KeyId,
            result.PlainTextKey, // Store this securely - won't be shown again
            result.CreatedAt,
            result.ExpiresAt
        });
    }

    [HttpGet]
    public async Task<IActionResult> ListKeys()
    {
        var ownerId = _contextAccessor.Context!.Principal.Identifier;
        var keys = await _apiKeyService.ListKeysAsync(ownerId);
        return Ok(keys);
    }

    [HttpDelete("{keyId}")]
    public async Task<IActionResult> RevokeKey(string keyId, [FromQuery] string? reason)
    {
        var success = await _apiKeyService.RevokeKeyAsync(keyId, reason);
        return success ? NoContent() : NotFound();
    }

    [HttpPost("{keyId}/rotate")]
    public async Task<IActionResult> RotateKey(string keyId)
    {
        var success = await _apiKeyService.RotateKeyAsync(keyId);
        return success ? Ok() : NotFound();
    }
}
```

#### Custom API Key Store

```csharp
public class DatabaseApiKeyStore : IApiKeyStore
{
    private readonly IDbConnection _db;
    private readonly TimeProvider _timeProvider;

    public async ValueTask<ApiKeyRecord?> GetByHashAsync(
        string keyHash,
        CancellationToken cancellationToken = default)
    {
        return await _db.QuerySingleOrDefaultAsync<ApiKeyRecord>(
            "SELECT * FROM api_keys WHERE key_hash = @Hash AND revoked_at IS NULL",
            new { Hash = keyHash });
    }

    public async ValueTask<bool> StoreAsync(
        ApiKeyRecord record,
        CancellationToken cancellationToken = default)
    {
        var affected = await _db.ExecuteAsync(
            @"INSERT INTO api_keys (id, key_hash, name, owner_id, tenant_id, 
              scopes, created_at, expires_at, metadata)
              VALUES (@Id, @KeyHash, @Name, @OwnerId, @TenantId, 
              @Scopes, @CreatedAt, @ExpiresAt, @Metadata)",
            record);
        return affected > 0;
    }

    // Implement other methods...
}
```

---

### MicroKit.Security.Cognito

AWS Cognito authentication provider.

#### Configuration

```csharp
builder.Services.AddCognitoProvider(options =>
{
    // Cognito User Pool settings
    options.Region = "us-east-1";
    options.UserPoolId = builder.Configuration["Cognito:UserPoolId"]!;
    options.ClientId = builder.Configuration["Cognito:ClientId"]!;
    options.ClientSecret = builder.Configuration["Cognito:ClientSecret"]; // Optional
    
    // Token validation
    options.ValidateIssuer = true;
    options.ValidateAudience = true;
    options.ValidateTokenUse = true; // Validates 'access' vs 'id' token
    options.TokenUse = CognitoTokenUse.Access;
    
    // JWKS caching
    options.JwksCacheDuration = TimeSpan.FromHours(24);
    options.JwksRefreshInterval = TimeSpan.FromHours(1);
    
    // Claim mapping
    options.MapCognitoGroups = true; // Map cognito:groups to roles
    options.GroupsClaimName = "cognito:groups";
    options.UsernameClaimName = "cognito:username";
    
    // Custom attribute mapping
    options.CustomAttributeMapping = new Dictionary<string, string>
    {
        ["custom:tenant_id"] = ClaimTypes.TenantId,
        ["custom:department"] = "department"
    };
});
```

#### ICognitoUserService

```csharp
public interface ICognitoUserService
{
    ValueTask<CognitoAuthResult> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default);
    
    ValueTask<CognitoAuthResult> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default);
    
    ValueTask<bool> SignOutAsync(
        string accessToken,
        CancellationToken cancellationToken = default);
    
    ValueTask<CognitoUser?> GetUserAsync(
        string accessToken,
        CancellationToken cancellationToken = default);
    
    ValueTask<bool> ChangePasswordAsync(
        string accessToken,
        string previousPassword,
        string proposedPassword,
        CancellationToken cancellationToken = default);
}
```

#### Usage with Cognito

```csharp
[ApiController]
[Route("api/auth")]
public class CognitoAuthController : ControllerBase
{
    private readonly ICognitoUserService _cognitoService;

    public CognitoAuthController(ICognitoUserService cognitoService)
    {
        _cognitoService = cognitoService;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var result = await _cognitoService.AuthenticateAsync(
            request.Username,
            request.Password);

        if (!result.Success)
            return Unauthorized(new { result.ErrorMessage });

        return Ok(new
        {
            result.AccessToken,
            result.IdToken,
            result.RefreshToken,
            result.ExpiresIn
        });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        var result = await _cognitoService.RefreshTokenAsync(request.RefreshToken);

        if (!result.Success)
            return Unauthorized(new { result.ErrorMessage });

        return Ok(new
        {
            result.AccessToken,
            result.IdToken,
            result.ExpiresIn
        });
    }

    [HttpPost("logout")]
    [RequireAuthentication]
    public async Task<IActionResult> Logout()
    {
        var accessToken = HttpContext.Request.Headers.Authorization
            .ToString().Replace("Bearer ", "");

        await _cognitoService.SignOutAsync(accessToken);
        return NoContent();
    }

    [HttpPost("change-password")]
    [RequireAuthentication]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request)
    {
        var accessToken = HttpContext.Request.Headers.Authorization
            .ToString().Replace("Bearer ", "");

        var success = await _cognitoService.ChangePasswordAsync(
            accessToken,
            request.CurrentPassword,
            request.NewPassword);

        return success ? NoContent() : BadRequest();
    }
}
```

---

### MicroKit.Security.AzureAd

Azure Active Directory / Microsoft Entra ID authentication provider.

#### Configuration

```csharp
builder.Services.AddAzureAdProvider(options =>
{
    // Azure AD tenant settings
    options.TenantId = builder.Configuration["AzureAd:TenantId"]!;
    options.ClientId = builder.Configuration["AzureAd:ClientId"]!;
    options.ClientSecret = builder.Configuration["AzureAd:ClientSecret"]; // For app auth
    options.Instance = "https://login.microsoftonline.com/"; // Default
    
    // Token validation
    options.ValidateIssuer = true;
    options.ValidateAudience = true;
    options.ValidAudiences = [options.ClientId, $"api://{options.ClientId}"];
    
    // Multi-tenant support
    options.AllowMultipleTenants = false;
    options.AllowedTenants = [options.TenantId];
    
    // Claim mapping
    options.MapAzureAdRoles = true;
    options.MapAzureAdGroups = true;
    options.GroupsClaimName = "groups";
    options.RolesClaimName = "roles";
    
    // Graph API integration
    options.EnableGraphApi = true;
    options.GraphApiScopes = ["User.Read", "GroupMember.Read.All"];
    
    // JWKS caching
    options.JwksCacheDuration = TimeSpan.FromHours(24);
});
```

#### IAzureAdGraphService

```csharp
public interface IAzureAdGraphService
{
    ValueTask<AzureAdUser?> GetUserAsync(
        string userId,
        CancellationToken cancellationToken = default);
    
    ValueTask<IReadOnlyList<string>> GetUserGroupsAsync(
        string userId,
        CancellationToken cancellationToken = default);
    
    ValueTask<IReadOnlyList<string>> GetUserRolesAsync(
        string userId,
        CancellationToken cancellationToken = default);
    
    ValueTask<AzureAdUser?> GetCurrentUserAsync(
        string accessToken,
        CancellationToken cancellationToken = default);
}
```

#### Usage with Azure AD

```csharp
[ApiController]
[Route("api/users")]
[RequireAuthentication]
public class UsersController : ControllerBase
{
    private readonly IAzureAdGraphService _graphService;
    private readonly IClientContextAccessor _contextAccessor;

    public UsersController(
        IAzureAdGraphService graphService,
        IClientContextAccessor contextAccessor)
    {
        _graphService = graphService;
        _contextAccessor = contextAccessor;
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUser()
    {
        var userId = _contextAccessor.Context!.Principal.Identifier!;
        
        var user = await _graphService.GetUserAsync(userId);
        var groups = await _graphService.GetUserGroupsAsync(userId);
        var roles = await _graphService.GetUserRolesAsync(userId);

        return Ok(new
        {
            User = user,
            Groups = groups,
            Roles = roles
        });
    }

    [HttpGet("{userId}/groups")]
    [RequirePermission("users:read")]
    public async Task<IActionResult> GetUserGroups(string userId)
    {
        var groups = await _graphService.GetUserGroupsAsync(userId);
        return Ok(groups);
    }
}
```

---

### MicroKit.Security.MultiTenancy

Multi-tenant support with flexible tenant resolution strategies.

#### Configuration

```csharp
builder.Services.AddMicroKitMultiTenancy(options =>
{
    // Resolution strategy (order matters)
    options.ResolutionStrategy = TenantResolutionStrategy.Header;
    options.FallbackStrategies = [
        TenantResolutionStrategy.Subdomain,
        TenantResolutionStrategy.Claim
    ];
    
    // Header-based resolution
    options.TenantHeaderName = "X-Tenant-Id";
    
    // Subdomain-based resolution
    options.SubdomainPosition = 0; // tenant.myapp.com
    options.BaseDomain = "myapp.com";
    
    // Path-based resolution
    options.TenantPathSegment = 0; // /tenant-id/api/resource
    
    // Claim-based resolution
    options.TenantClaimType = ClaimTypes.TenantId;
    
    // Validation
    options.RequireTenant = true;
    options.ValidateTenantExists = true;
    options.CacheTenantInfo = true;
    options.TenantCacheDuration = TimeSpan.FromMinutes(30);
    
    // Default tenant (for requests without tenant)
    options.DefaultTenantId = null;
    
    // Isolation
    options.EnforceIsolation = true;
});

// Add tenant store
builder.Services.AddInMemoryTenantStore();
// Or custom store:
// builder.Services.AddSingleton<ITenantStore, DatabaseTenantStore>();
```

#### ITenantContext

```csharp
public interface ITenantContext
{
    TenantInfo? CurrentTenant { get; }
    string? TenantId { get; }
    bool HasTenant { get; }
    bool IsDefaultTenant { get; }
    
    T? GetSetting<T>(string key);
    string? GetConnectionString(string name = "Default");
}
```

#### Tenant-Aware Services

```csharp
public class TenantAwareOrderRepository : IOrderRepository
{
    private readonly ITenantContext _tenantContext;
    private readonly IDbConnectionFactory _connectionFactory;

    public TenantAwareOrderRepository(
        ITenantContext tenantContext,
        IDbConnectionFactory connectionFactory)
    {
        _tenantContext = tenantContext;
        _connectionFactory = connectionFactory;
    }

    public async Task<Order?> GetByIdAsync(Guid id)
    {
        if (!_tenantContext.HasTenant)
            throw new InvalidOperationException("Tenant context required");

        // Get tenant-specific connection string
        var connectionString = _tenantContext.GetConnectionString();
        
        using var connection = _connectionFactory.Create(connectionString);
        
        return await connection.QuerySingleOrDefaultAsync<Order>(
            "SELECT * FROM orders WHERE id = @Id AND tenant_id = @TenantId",
            new { Id = id, TenantId = _tenantContext.TenantId });
    }

    public async Task<IReadOnlyList<Order>> GetAllAsync()
    {
        var connectionString = _tenantContext.GetConnectionString();
        using var connection = _connectionFactory.Create(connectionString);
        
        var orders = await connection.QueryAsync<Order>(
            "SELECT * FROM orders WHERE tenant_id = @TenantId",
            new { TenantId = _tenantContext.TenantId });
        
        return orders.ToList();
    }
}
```

#### Managing Tenants

```csharp
[ApiController]
[Route("api/admin/tenants")]
[RequireRole("system_admin")]
public class TenantsController : ControllerBase
{
    private readonly ITenantStore _tenantStore;

    public TenantsController(ITenantStore tenantStore)
    {
        _tenantStore = tenantStore;
    }

    [HttpPost]
    public async Task<IActionResult> CreateTenant([FromBody] CreateTenantRequest request)
    {
        var tenant = new TenantInfo
        {
            Id = Guid.NewGuid().ToString(),
            Identifier = request.Identifier,
            Name = request.Name,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            Settings = new Dictionary<string, object>
            {
                ["plan"] = request.Plan,
                ["maxUsers"] = request.MaxUsers
            },
            ConnectionStrings = new Dictionary<string, string>
            {
                ["Default"] = $"Server=db;Database=tenant_{request.Identifier};..."
            }
        };

        await _tenantStore.CreateAsync(tenant);
        return Created($"/api/admin/tenants/{tenant.Id}", tenant);
    }

    [HttpPut("{tenantId}/deactivate")]
    public async Task<IActionResult> DeactivateTenant(string tenantId)
    {
        var tenant = await _tenantStore.GetByIdAsync(tenantId);
        if (tenant is null)
            return NotFound();

        var updated = tenant with { IsActive = false };
        await _tenantStore.UpdateAsync(updated);
        return NoContent();
    }
}
```

---

### MicroKit.Security.Messaging

Secure context propagation for message-based architectures.

#### MassTransit Integration

```csharp
builder.Services.AddMicroKitMessagingSecurity(options =>
{
    // Header names
    options.CorrelationIdHeader = "X-Correlation-Id";
    options.UserIdHeader = "X-User-Id";
    options.TenantIdHeader = "X-Tenant-Id";
    options.SecurityContextHeader = "X-Security-Context";
    
    // Signing (for message integrity)
    options.EnableMessageSigning = true;
    options.SigningKey = builder.Configuration["Messaging:SigningKey"]!;
    options.SignatureHeader = "X-Message-Signature";
    
    // Encryption
    options.EnableEncryption = false;
    options.EncryptionKey = builder.Configuration["Messaging:EncryptionKey"];
    
    // Validation
    options.ValidateIncomingSignatures = true;
    options.RejectUnsignedMessages = false;
    options.MaxMessageAge = TimeSpan.FromMinutes(5);
});

// Add MassTransit with security filters
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<OrderCreatedConsumer>();
    
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("localhost", "/", h =>
        {
            h.Username("guest");
            h.Password("guest");
        });

        // Add security filters
        cfg.UseMicroKitSecurityFilters(context);
        
        cfg.ConfigureEndpoints(context);
    });
});
```

#### Message Consumer with Security Context

```csharp
public class OrderCreatedConsumer : IConsumer<OrderCreated>
{
    private readonly IClientContextAccessor _contextAccessor;
    private readonly ILogger<OrderCreatedConsumer> _logger;

    public OrderCreatedConsumer(
        IClientContextAccessor contextAccessor,
        ILogger<OrderCreatedConsumer> logger)
    {
        _contextAccessor = contextAccessor;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OrderCreated> context)
    {
        // Security context is automatically restored from message headers
        var clientContext = _contextAccessor.Context;
        
        _logger.LogInformation(
            "Processing order {OrderId} for user {UserId} in tenant {TenantId}",
            context.Message.OrderId,
            clientContext?.Principal.Identifier,
            clientContext?.TenantId);

        // Process order with full security context...
    }
}
```

#### Publishing Messages with Security Context

```csharp
public class OrderService
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly IClientContextAccessor _contextAccessor;

    public OrderService(
        IPublishEndpoint publishEndpoint,
        IClientContextAccessor contextAccessor)
    {
        _publishEndpoint = publishEndpoint;
        _contextAccessor = contextAccessor;
    }

    public async Task CreateOrderAsync(CreateOrderRequest request)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            // ... order details
        };

        // Save order...

        // Publish event - security context is automatically propagated
        await _publishEndpoint.Publish(new OrderCreated
        {
            OrderId = order.Id,
            CustomerId = request.CustomerId,
            Total = request.Total
        });
    }
}
```

#### Azure Service Bus Integration

```csharp
builder.Services.AddMicroKitMessagingSecurity(options =>
{
    options.EnableMessageSigning = true;
    options.SigningKey = builder.Configuration["Messaging:SigningKey"]!;
});

// Configure Azure Service Bus processor
builder.Services.AddSingleton(sp =>
{
    var client = new ServiceBusClient(connectionString);
    var processor = client.CreateProcessor("orders-queue");
    
    // Add security processing
    var securityProcessor = sp.GetRequiredService<ServiceBusSecurityProcessor>();
    
    processor.ProcessMessageAsync += async args =>
    {
        // Restore security context from message
        using var scope = securityProcessor.CreateSecurityScope(args.Message);
        
        // Process message with security context available
        await ProcessMessageAsync(args.Message, args.CancellationToken);
    };
    
    return processor;
});
```

#### Sending Messages with Azure Service Bus

```csharp
public class ServiceBusMessageSender
{
    private readonly ServiceBusSender _sender;
    private readonly ISecurityContextPropagator _propagator;
    private readonly IClientContextAccessor _contextAccessor;

    public async Task SendAsync<T>(T message, CancellationToken cancellationToken = default)
    {
        var serviceBusMessage = new ServiceBusMessage(
            BinaryData.FromObjectAsJson(message));

        // Propagate security context to message headers
        if (_contextAccessor.HasContext)
        {
            _propagator.InjectContext(
                _contextAccessor.Context!,
                serviceBusMessage.ApplicationProperties,
                (props, key, value) => props[key] = value);
        }

        await _sender.SendMessageAsync(serviceBusMessage, cancellationToken);
    }
}
```

---

## Configuration Reference

### appsettings.json Example

```json
{
  "MicroKitSecurity": {
    "DefaultScheme": "Jwt",
    "RequireAuthentication": true,
    "AnonymousPaths": ["/health", "/metrics", "/swagger"],
    "Cache": {
      "Enabled": true,
      "DefaultExpiration": "00:05:00",
      "MaxSize": 10000
    }
  },
  
  "Jwt": {
    "SecretKey": "your-256-bit-secret-key-here-min-32-chars",
    "Issuer": "https://auth.myapp.com",
    "Audience": "myapp-api",
    "TokenLifetimeMinutes": 60,
    "RefreshTokenLifetimeDays": 7
  },
  
  "ApiKey": {
    "HeaderName": "X-API-Key",
    "KeyPrefix": "mk_",
    "EnableRateLimiting": true,
    "RateLimitPerMinute": 1000
  },
  
  "Cognito": {
    "Region": "us-east-1",
    "UserPoolId": "us-east-1_xxxxxxxx",
    "ClientId": "xxxxxxxxxxxxxxxxxxxxxxxxxx",
    "ClientSecret": "optional-client-secret"
  },
  
  "AzureAd": {
    "TenantId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
    "ClientId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
    "ClientSecret": "your-client-secret"
  },
  
  "MultiTenancy": {
    "ResolutionStrategy": "Header",
    "TenantHeaderName": "X-Tenant-Id",
    "RequireTenant": true,
    "ValidateTenantExists": true
  },
  
  "Messaging": {
    "SigningKey": "your-message-signing-key-min-32-chars",
    "EnableSigning": true,
    "ValidateSignatures": true
  }
}
```

### Environment Variables

```bash
# JWT Configuration
MICROKIT_JWT_SECRET_KEY=your-secret-key
MICROKIT_JWT_ISSUER=https://auth.myapp.com
MICROKIT_JWT_AUDIENCE=myapp-api

# AWS Cognito
AWS_COGNITO_REGION=us-east-1
AWS_COGNITO_USER_POOL_ID=us-east-1_xxxxxxxx
AWS_COGNITO_CLIENT_ID=xxxxxxxxxxxxxxxxxxxxxxxxxx

# Azure AD
AZURE_AD_TENANT_ID=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
AZURE_AD_CLIENT_ID=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
AZURE_AD_CLIENT_SECRET=your-client-secret

# Messaging
MICROKIT_MESSAGING_SIGNING_KEY=your-signing-key
```

---

## Advanced Scenarios

### Custom Authentication Provider

```csharp
public class LdapAuthenticationProvider : IAuthenticationProvider
{
    public AuthenticationScheme Scheme => AuthenticationScheme.None; // Custom
    public int Priority => 100;

    private readonly LdapOptions _options;
    private readonly TimeProvider _timeProvider;

    public LdapAuthenticationProvider(
        IOptions<LdapOptions> options,
        TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public bool CanHandle(string credential)
    {
        // Check if this looks like LDAP credentials
        return credential.StartsWith("LDAP ");
    }

    public async ValueTask<AuthenticationResult> AuthenticateAsync(
        string credential,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var token = credential["LDAP ".Length..];
            var (username, password) = ParseCredentials(token);

            // Validate against LDAP
            var user = await ValidateLdapUserAsync(username, password);
            
            if (user is null)
            {
                return AuthenticationResult.Failure(
                    ValidationStatus.Invalid,
                    "Invalid LDAP credentials");
            }

            var principal = new SecurityPrincipal(
                Identifier: user.Dn,
                DisplayName: user.DisplayName,
                Claims: user.Claims);

            return AuthenticationResult.Success(principal, Scheme);
        }
        catch (Exception ex)
        {
            return AuthenticationResult.Failure(
                ValidationStatus.Invalid,
                ex.Message);
        }
    }
}

// Register the provider
builder.Services.AddSingleton<IAuthenticationProvider, LdapAuthenticationProvider>();
```

### Composite Authentication

```csharp
builder.Services
    .AddMicroKitSecurity(options =>
    {
        options.DefaultScheme = AuthenticationScheme.Jwt;
        options.FallbackSchemes = [
            AuthenticationScheme.ApiKey,
            AuthenticationScheme.Cognito
        ];
        options.RequireAuthentication = true;
    })
    .AddJwtProvider(options => { /* ... */ })
    .AddApiKeyProvider(options => { /* ... */ })
    .AddCognitoProvider(options => { /* ... */ });
```

### Custom Tenant Resolver

```csharp
public class DatabaseTenantResolver : ITenantResolutionStrategy
{
    private readonly IDbConnection _db;
    
    public TenantResolutionStrategy Strategy => 
        TenantResolutionStrategy.Custom;
    
    public int Priority => 0; // Highest priority

    public async ValueTask<string?> ResolveAsync(
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        // Resolve tenant from custom header containing domain
        var domain = context.Request.Headers["X-Domain"].FirstOrDefault();
        
        if (string.IsNullOrEmpty(domain))
            return null;

        var tenantId = await _db.QuerySingleOrDefaultAsync<string>(
            "SELECT tenant_id FROM tenant_domains WHERE domain = @Domain",
            new { Domain = domain });

        return tenantId;
    }
}

// Register custom resolver
builder.Services.AddSingleton<ITenantResolutionStrategy, DatabaseTenantResolver>();
```

### Rate Limiting with API Keys

```csharp
builder.Services.AddApiKeyProvider(options =>
{
    options.EnableRateLimiting = true;
    options.RateLimitPerMinute = 1000;
    
    // Custom rate limit per key tier
    options.RateLimitResolver = (apiKeyRecord) =>
    {
        return apiKeyRecord.Metadata.TryGetValue("tier", out var tier)
            ? tier switch
            {
                "free" => 100,
                "pro" => 1000,
                "enterprise" => 10000,
                _ => 100
            }
            : 100;
    };
});
```

---

## Best Practices

### 1. Always Use Dependency Injection

```csharp
// Good
public class MyService
{
    private readonly IClientContextAccessor _accessor;
    
    public MyService(IClientContextAccessor accessor)
    {
        _accessor = accessor;
    }
}

// Bad - Avoid service locator pattern
public class MyService
{
    public void DoSomething(IServiceProvider provider)
    {
        var accessor = provider.GetRequiredService<IClientContextAccessor>();
    }
}
```

### 2. Check Authentication Before Accessing Context

```csharp
public async Task<Result> ProcessAsync()
{
    if (!_contextAccessor.HasContext || !_contextAccessor.Context!.IsAuthenticated)
    {
        return Result.Unauthorized();
    }
    
    var userId = _contextAccessor.Context.Principal.Identifier;
    // Continue processing...
}
```

### 3. Use Strong Typing for Claims

```csharp
// Define typed accessors
public static class PrincipalExtensions
{
    public static Guid GetUserId(this ISecurityPrincipal principal)
    {
        var id = principal.GetClaimValue(ClaimTypes.Subject);
        return Guid.TryParse(id, out var userId) ? userId : Guid.Empty;
    }
    
    public static UserRole GetRole(this ISecurityPrincipal principal)
    {
        var role = principal.GetClaimValue(ClaimTypes.Role);
        return Enum.TryParse<UserRole>(role, out var result) 
            ? result 
            : UserRole.Unknown;
    }
}
```

### 4. Secure Key Storage

```csharp
// Good - Use secret management
builder.Services.AddJwtProvider(options =>
{
    options.SecretKey = builder.Configuration["Jwt:SecretKey"]!;
});

// In production, use Azure Key Vault, AWS Secrets Manager, etc.
builder.Configuration.AddAzureKeyVault(
    new Uri($"https://{vaultName}.vault.azure.net/"),
    new DefaultAzureCredential());
```

### 5. Implement Proper Error Handling

```csharp
builder.Services.AddMicroKitAspNetCore(options =>
{
    options.OnAuthenticationFailed = async context =>
    {
        context.Response.StatusCode = 401;
        context.Response.ContentType = "application/problem+json";
        
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = 401,
            Title = "Authentication Failed",
            Detail = context.Environment.IsDevelopment() 
                ? context.Failure?.Message 
                : "Invalid or expired credentials",
            Instance = context.Request.Path
        });
    };
});
```

### 6. Use Caching Appropriately

```csharp
builder.Services.AddMicroKitSecurity(options =>
{
    options.EnableCaching = true;
    options.Cache = new CacheOptions
    {
        // Short expiration for access tokens
        DefaultExpiration = TimeSpan.FromMinutes(5),
        
        // Use sliding expiration for active users
        SlidingExpiration = true,
        
        // Limit cache size to prevent memory issues
        MaxCacheSize = 10000
    };
});
```

---

## Troubleshooting

### Common Issues

#### 1. "No authentication provider found for scheme"

```csharp
// Ensure the provider is registered
builder.Services
    .AddMicroKitSecurity(options =>
    {
        options.DefaultScheme = AuthenticationScheme.Jwt; // Must match
    })
    .AddJwtProvider(options => { /* ... */ }); // Provider must be added
```

#### 2. "Token validation failed"

```csharp
// Check issuer/audience configuration
builder.Services.AddJwtProvider(options =>
{
    options.Issuer = "https://auth.myapp.com"; // Must match token's 'iss'
    options.Audience = "myapp-api"; // Must match token's 'aud'
    options.ClockSkew = TimeSpan.FromMinutes(5); // Allow clock drift
});
```

#### 3. "Tenant not found"

```csharp
// Check tenant resolution strategy
builder.Services.AddMicroKitMultiTenancy(options =>
{
    // Ensure header name matches client
    options.TenantHeaderName = "X-Tenant-Id";
    
    // Or disable requirement for testing
    options.RequireTenant = false;
});
```

#### 4. "Security context not propagated in messages"

```csharp
// Ensure filters are registered
cfg.UsingRabbitMq((context, cfg) =>
{
    // This must be called!
    cfg.UseMicroKitSecurityFilters(context);
    
    cfg.ConfigureEndpoints(context);
});
```

### Debugging

Enable detailed logging:

```csharp
builder.Logging.AddFilter("MicroKit.Security", LogLevel.Debug);
```

Check the client context:

```csharp
app.Use(async (context, next) =>
{
    var accessor = context.RequestServices
        .GetRequiredService<IClientContextAccessor>();
    
    Console.WriteLine($"Has Context: {accessor.HasContext}");
    Console.WriteLine($"Is Authenticated: {accessor.Context?.IsAuthenticated}");
    Console.WriteLine($"User ID: {accessor.Context?.Principal.Identifier}");
    Console.WriteLine($"Tenant ID: {accessor.Context?.TenantId}");
    
    await next();
});
```

---

## License

MIT License - See LICENSE file for details.

## Contributing

Contributions are welcome! Please read CONTRIBUTING.md for guidelines.

## Support

- GitHub Issues: [Report bugs and feature requests](https://github.com/microkit/security/issues)
- Documentation: [Full API documentation](https://docs.microkit.dev/security)
