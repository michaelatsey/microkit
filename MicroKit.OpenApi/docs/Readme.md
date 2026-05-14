# MicroKit.OpenApi

OpenAPI documentation and Scalar UI integration for ASP.NET Core — multi-version, pluggable security schemes, extensible filters.

---

## Overview

`MicroKit.OpenApi` wraps Microsoft's native OpenAPI infrastructure (`Microsoft.AspNetCore.OpenApi`) and adds:

- **Multi-version document generation** — one OpenAPI JSON document per API version, auto-registered.
- **Scalar UI** — modern API explorer replacing Swagger UI, with theme and layout control.
- **Security schemes** — Bearer/JWT, OAuth 2.0 (all four flows), and API Key out of the box.
- **Extensible filter pipeline** — document, operation, and schema filters for custom transformations.
- **Configuration-driven** — all options bindable from `appsettings.json`.
- **Fluent builder API** — chainable setup without touching raw `IServiceCollection`.

---

## Installation

```bash
dotnet add package MicroKit.OpenApi
```

Requires .NET 10 / ASP.NET Core 10.

---

## Quickstart

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMicroKitOpenApi(builder.Configuration)
    .AddBearerSecurity()
    .AddDocumentFilter<DeprecationDocumentFilter>()
    .ConfigureScalar(s => { s.Theme = ScalarTheme.Moon; s.DarkMode = true; });

var app = builder.Build();
app.UseMicroKitOpenApi();
app.Run();
```

OpenAPI documents are served at `/openapi/{documentName}.json`.  
The Scalar UI is served at `/scalar/{documentName}`.

---

## Configuration — `appsettings.json`

```json
{
  "MicroKit:OpenApi": {
    "Title": "Order Service",
    "Description": "Manages orders in the platform.",
    "DefaultVersion": "2.0",
    "SupportedVersions": ["1.0", "2.0"],
    "DeprecatedVersions": ["0.9"],
    "EnableScalar": true,
    "Theme": "Moon",
    "ScalarEndpointPath": "/scalar/{documentName}",
    "OpenApiEndpointPath": "/openapi/{documentName}.json",
    "ApiVersionHeaderKey": "X-Api-Version",
    "ApiVersionQueryKey": "api-version",
    "Contact": {
      "Name": "API Team",
      "Email": "api@example.com",
      "Url": "https://example.com/support"
    },
    "License": {
      "Name": "MIT",
      "Url": "https://opensource.org/licenses/MIT"
    },
    "TermsOfServiceUrl": "https://example.com/terms",
    "Servers": [
      { "Url": "https://api.example.com", "Description": "Production" },
      { "Url": "https://staging.example.com", "Description": "Staging" }
    ]
  }
}
```

---

## Fluent Builder API

All overloads return `IMicroKitOpenApiBuilder` for chaining.

```csharp
builder.Services
    .AddMicroKitOpenApi(builder.Configuration)          // bind from appsettings.json
    .AddMicroKitOpenApi(options => { ... })             // code-only
    .AddMicroKitOpenApi(builder.Configuration, options => { ... }); // both
```

### Version management

```csharp
builder.Services.AddMicroKitOpenApi(options =>
{
    options.Title = "My API";
    options.DefaultVersion = "2.0";
    options.SupportedVersions = ["1.0", "2.0"];
    options.DeprecatedVersions = ["0.9"];
})
// Or add versions via builder:
.AddVersion("3.0")
.AddVersion("0.8", deprecated: true)
// Or register explicit OpenAPI documents for specific versions:
.WithVersionDocuments("1.0", "2.0", "0.9")
// Or read versions from configuration automatically:
.WithVersionDocumentsFromConfig(builder.Configuration);
```

### Server URLs

```csharp
.AddServer("https://api.example.com", "Production")
.AddServer("https://staging.example.com", "Staging")
```

### Scalar UI

```csharp
.ConfigureScalar(s =>
{
    s.Theme = ScalarTheme.DeepSpace;
    s.DarkMode = true;
    s.ShowSidebar = true;
    s.ShowDownloadButton = true;
    s.Favicon = "/favicon.ico";
    s.CustomCss = "body { font-family: sans-serif; }";
})
```

Available themes: `Default`, `Alternate`, `Moon`, `Purple`, `Solarized`, `BluePlanet`, `Saturn`, `Kepler`, `Mars`, `DeepSpace`.

---

## Security Schemes

Security schemes are registered via `IMicroKitOpenApiBuilder` and appear in both the OpenAPI document and the Scalar UI.

### Bearer / JWT

```csharp
.AddBearerSecurity(options =>
{
    options.SchemeName = "Bearer";          // default
    options.BearerFormat = "JWT";           // default
    options.Description = "Enter your JWT";
    options.PrefilledValue = "dev-token";   // development only
})
```

### API Key

```csharp
.AddApiKeySecurity(options =>
{
    options.Name = "X-Api-Key";             // header/query param name
    options.Location = ApiKeyLocation.Header; // Header | Query | Cookie
    options.SchemeName = "ApiKey";          // default
    options.PrefilledValue = "dev-key";     // development only
})
```

### OAuth 2.0

```csharp
.AddOAuth2Security(options =>
{
    options.SchemeName = "Keycloak";
    options.FlowType = OAuth2FlowType.AuthorizationCode; // or ClientCredentials, Password, Implicit
    options.AuthorizationUrl = "https://auth.example.com/authorize";
    options.TokenUrl = "https://auth.example.com/token";
    options.EnablePkce = true;
    options.Scopes = new Dictionary<string, string>
    {
        ["api:read"]  = "Read access",
        ["api:write"] = "Write access"
    };
    options.PrefilledClientId = "my-client"; // development only
})
```

### Security schemes from `appsettings.json`

Use the `Type` discriminator (`Bearer`, `ApiKey`, `OAuth2`) to configure schemes declaratively:

```json
"Securities": [
  {
    "Type": "Bearer",
    "SchemeName": "UserAuth",
    "Description": "JWT for end-users"
  },
  {
    "Type": "ApiKey",
    "SchemeName": "ServiceKey",
    "Name": "X-Internal-Key",
    "Location": "Header"
  },
  {
    "Type": "OAuth2",
    "SchemeName": "Keycloak",
    "FlowType": "AuthorizationCode",
    "AuthorizationUrl": "https://auth.example.com/authorize",
    "TokenUrl": "https://auth.example.com/token",
    "Scopes": { "api__read": "Read access" }
  }
]
```

> **Note:** Use double-underscore (`__`) in JSON scope keys as a stand-in for colon (`:`). The library rewrites them automatically (e.g., `api__read` → `api:read`).

---

## Extensible Filters

Implement a filter interface and register it via the builder.

### Document filter

```csharp
public sealed class CustomDocumentFilter : IOpenApiDocumentFilter
{
    public Task ApplyAsync(OpenApiDocument document, DocumentFilterContext context, CancellationToken ct = default)
    {
        document.Info.Description += "\n\nCustom footer added by document filter.";
        return Task.CompletedTask;
    }
}

// Registration
builder.Services.AddMicroKitOpenApi(config)
    .AddDocumentFilter<CustomDocumentFilter>();
```

### Operation filter

```csharp
public sealed class CustomOperationFilter : IOpenApiOperationFilter
{
    public Task ApplyAsync(OpenApiOperation operation, OperationFilterContext context, CancellationToken ct = default)
    {
        operation.Summary ??= "No summary provided";
        return Task.CompletedTask;
    }
}

.AddOperationFilter<CustomOperationFilter>()
```

### Schema filter

```csharp
public sealed class CustomSchemaFilter : IOpenApiSchemaFilter
{
    public Task ApplyAsync(OpenApiSchema schema, SchemaFilterContext context, CancellationToken ct = default)
    {
        if (context.Type == typeof(DateTime))
            schema.Format = "date-time";
        return Task.CompletedTask;
    }
}

.AddSchemaFilter<CustomSchemaFilter>()
```

---

## Built-in Filters

### `DeprecationDocumentFilter`

Marks all operations in a deprecated API version document as deprecated and prepends a deprecation banner to the document description.

```csharp
.AddDocumentFilter<DeprecationDocumentFilter>()
```

### `RequiredSchemaFilter`

Promotes properties annotated with `[Required]` to the OpenAPI `required` array. Respects `[JsonPropertyName]` for camelCase mapping.

```csharp
.AddSchemaFilter<RequiredSchemaFilter>()
```

### `ExamplesOperationFilter`

Reads `[OpenApiResponseExample]` attributes from endpoint methods and injects JSON examples into the corresponding response.

```csharp
[OpenApiResponseExample(200, """{"id": 1, "name": "Widget"}""")]
public IActionResult Get() => Ok(new Widget());

.AddOperationFilter<ExamplesOperationFilter>()
```

---

## API Versioning

`MicroKit.OpenApi` configures `Asp.Versioning` automatically. Each version produces a separate OpenAPI document.

### Minimal API

```csharp
var v1 = app.NewVersionedApi("Orders")
    .MapGroup("/api/v{version:apiVersion}/orders")
    .HasApiVersion(1.0);

v1.MapGet("/", () => Results.Ok("v1 orders"));

var v2 = app.NewVersionedApi("Orders")
    .MapGroup("/api/v{version:apiVersion}/orders")
    .HasApiVersion(2.0);

v2.MapGet("/", () => Results.Ok(new { Orders = "v2 orders", Count = 1 }));
```

### Controller

```csharp
[ApiController]
[ApiVersion("1.0")]
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class ProductsController : ControllerBase
{
    [HttpGet, MapToApiVersion("1.0")]
    public IActionResult GetV1() => Ok(new { Version = "1.0" });

    [HttpGet, MapToApiVersion("2.0")]
    public IActionResult GetV2() => Ok(new { Version = "2.0", Enhanced = true });
}
```

Version readers configured by default: URL segment (`/v{version}`), query string (`?api-version=`), header (`X-Api-Version`), media type.

---

## Startup Validation

`MicroKit.OpenApi` registers `IValidateOptions<MicroKitOpenApiOptions>` and validates configuration at startup:

- `Title` is required.
- `DefaultVersion` is required and must appear in `SupportedVersions` or `DeprecatedVersions`.
- At least one version must be configured.
- Contact email must be a valid address when set.
- OAuth 2.0 flows require the appropriate URLs (`AuthorizationUrl`, `TokenUrl`).
- Security `SchemeName` values must be unique.

Invalid configuration causes a descriptive exception before the application starts — safe for Docker / Kubernetes / CI-CD.

---

## Endpoints

| Path | Description |
|------|-------------|
| `/openapi/{documentName}.json` | OpenAPI specification JSON for a specific document |
| `/scalar/{documentName}` | Scalar UI for a specific document |

The default document for the Scalar UI is the `DefaultVersion` from options.

---

## Dependencies

| Package | Purpose |
|---------|---------|
| `Microsoft.AspNetCore.OpenApi` | Native ASP.NET Core OpenAPI document generation |
| `Asp.Versioning.Http` | HTTP API versioning |
| `Asp.Versioning.Mvc.ApiExplorer` | API Explorer integration for versioned endpoints |
| `Scalar.AspNetCore` | Scalar UI endpoint and configuration |
| `Scalar.AspNetCore.Microsoft` | Microsoft OpenAPI adapter for Scalar |

---

## License

MIT
