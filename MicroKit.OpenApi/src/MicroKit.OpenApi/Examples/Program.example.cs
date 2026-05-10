// =============================================================================
// EXAMPLE: How to use MicroKit.OpenApi in your ASP.NET Core application
// =============================================================================
// This file is for documentation purposes only. Copy the relevant parts to your Program.cs.

#if false // This is example code - not compiled

using MicroKit.OpenApi.Extensions;
using MicroKit.OpenApi.Filters;

var builder = WebApplication.CreateBuilder(args);

// =============================================================================
// OPTION 1: Configuration from appsettings.json + explicit version documents
// =============================================================================
// This is the recommended approach - NO BuildServiceProvider() anti-pattern!

builder.Services.AddMicroKitOpenApi(builder.Configuration)
    .WithVersionDocuments("1.0", "2.0", "0.9")  // Register OpenAPI docs for each version
    .AddBearerSecurity()
    .AddDocumentFilter<DeprecationDocumentFilter>()
    .ConfigureScalar(options =>
    {
        options.DarkMode = true;
    });

// =============================================================================
// OPTION 2: Configuration from appsettings.json + auto-detect versions
// =============================================================================
// Reads versions from configuration file

builder.Services.AddMicroKitOpenApi(builder.Configuration)
    .WithVersionDocumentsFromConfig(builder.Configuration)
    .AddBearerSecurity();

// =============================================================================
// OPTION 3: Pure code-based configuration
// =============================================================================

builder.Services.AddMicroKitOpenApi(options =>
{
    options.Title = "Order Service API";
    options.Description = "Manages orders in the system";
    options.DefaultVersion = "1.0";
    options.SupportedVersions = ["1.0", "2.0"];
    options.DeprecatedVersions = ["0.9"];
    options.EnableScalar = true;
    options.RoutingStyle = ApiRoutingStyle.Both; // Works with Minimal APIs AND Controllers
    
    options.Contact = new ContactOptions
    {
        Name = "API Team",
        Email = "api@company.com"
    };
})
.WithVersionDocuments("1.0", "2.0", "0.9")
.AddBearerSecurity()
.AddApiKeySecurity(apiKey =>
{
    apiKey.Name = "X-Api-Key";
    apiKey.Location = ApiKeyLocation.Header;
})
.AddOAuth2Security(oauth =>
{
    oauth.AuthorizationUrl = "https://auth.company.com/authorize";
    oauth.TokenUrl = "https://auth.company.com/token";
    oauth.FlowType = OAuth2FlowType.AuthorizationCode;
    oauth.Scopes = new Dictionary<string, string>
    {
        ["read"] = "Read access",
        ["write"] = "Write access"
    };
});

// =============================================================================
// OPTION 4: Hybrid - appsettings.json + code overrides
// =============================================================================

builder.Services.AddMicroKitOpenApi(builder.Configuration, options =>
{
    // Override or supplement appsettings.json values
    options.Title = "My Custom Title";
})
.WithVersionDocumentsFromConfig(builder.Configuration)
.AddVersion("3.0")  // Add additional version via fluent API
.AddBearerSecurity();

// =============================================================================
// BUILD AND CONFIGURE PIPELINE
// =============================================================================

var app = builder.Build();

// Enable MicroKit OpenAPI endpoints (OpenAPI JSON + Scalar UI)
app.UseMicroKitOpenApi();

// Or with custom Scalar configuration
app.UseMicroKitOpenApi(scalar =>
{
    scalar.DarkMode = false;
    scalar.ShowSidebar = true;
});

// =============================================================================
// MINIMAL API EXAMPLE
// =============================================================================

var v1 = app.NewVersionedApi("Orders")
    .MapGroup("/api/v{version:apiVersion}/orders")
    .HasApiVersion(1.0);

v1.MapGet("/", () => Results.Ok(new[] { "Order1", "Order2" }))
    .WithName("GetOrders")
    .WithSummary("Get all orders");

var v2 = app.NewVersionedApi("Orders")
    .MapGroup("/api/v{version:apiVersion}/orders")
    .HasApiVersion(2.0);

v2.MapGet("/", () => Results.Ok(new { Orders = new[] { "Order1", "Order2" }, Count = 2 }))
    .WithName("GetOrdersV2")
    .WithSummary("Get all orders with count");

// =============================================================================
// CONTROLLER EXAMPLE (in separate file)
// =============================================================================
/*
[ApiController]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
[ApiVersion("2.0")]
public class ProductsController : ControllerBase
{
    [HttpGet]
    [MapToApiVersion("1.0")]
    public IActionResult GetV1() => Ok(new[] { "Product1" });
    
    [HttpGet]
    [MapToApiVersion("2.0")]
    public IActionResult GetV2() => Ok(new { Products = new[] { "Product1" }, Count = 1 });
}
*/

app.Run();

#endif
