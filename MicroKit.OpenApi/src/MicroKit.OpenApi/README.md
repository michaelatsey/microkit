# MicroKit.OpenApi

Enterprise-grade OpenAPI documentation library for ASP.NET Core microservices using Scalar UI.

## Features

- **Scalar UI Integration** - Modern, beautiful API documentation UI with multiple themes
- **API Versioning** - Built-in support for Asp.Versioning with multi-version documentation
- **Security Schemes** - JWT Bearer, OAuth2, and API Key authentication support
- **Extensible Filters** - Document, Operation, and Schema filters for customization
- **Configuration-Driven** - Full support for `appsettings.json` configuration
- **Microsoft.OpenApi 2.0+** - Compatible with .NET 10 and latest OpenAPI libraries

## Installation

```bash
dotnet add package MicroKit.OpenApi
```

## Quick Start

### Minimal Setup

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMicroKitOpenApi(builder.Configuration);

var app = builder.Build();

app.UseMicroKitOpenApi();

app.Run();
```

### Configuration via appsettings.json

```json
{
  "MicroKitOpenApi": {
    "Title": "My Awesome API",
    "Description": "A comprehensive REST API for my application",
    "DefaultVersion": "1.0",
    "SupportedVersions": ["1.0", "2.0"],
    "DeprecatedVersions": ["0.9"],
    "EnableScalar": true,
    "Theme": "Moon",
    "Contact": {
      "Name": "API Support",
      "Email": "support@example.com",
      "Url": "https://example.com/support"
    },
    "License": {
      "Name": "MIT",
      "Url": "https://opensource.org/licenses/MIT"
    },
    "Servers": [
      {
        "Url": "https://api.example.com",
        "Description": "Production"
      },
      {
        "Url": "https://staging-api.example.com",
        "Description": "Staging"
      }
    ]
  }
}
```

## Advanced Configuration

### Fluent Builder API

```csharp
builder.Services.AddMicroKitOpenApi(builder.Configuration)
    .Configure(options =>
    {
        options.Title = "My API";
        options.Description = "API Description";
    })
    .AddVersion("1.0")
    .AddVersion("2.0")
    .AddVersion("0.9", deprecated: true)
    .AddServer("https://api.example.com", "Production")
    .AddServer("https://staging.example.com", "Staging")
    .AddBearerSecurity()
    .ConfigureScalar(scalar =>
    {
        scalar.Theme = ScalarTheme.Moon;
        scalar.DarkMode = true;
        scalar.ShowSidebar = true;
    });
```

### Security Configuration

#### JWT Bearer Authentication

```csharp
builder.Services.AddMicroKitOpenApi(builder.Configuration)
    .AddBearerSecurity(options =>
    {
        options.SchemeName = "Bearer";
        options.Description = "Enter your JWT token";
        options.BearerFormat = "JWT";
    });
```

#### OAuth2 Authentication

```csharp
builder.Services.AddMicroKitOpenApi(builder.Configuration)
    .AddOAuth2Security(options =>
    {
        options.AuthorizationUrl = "https://auth.example.com/authorize";
        options.TokenUrl = "https://auth.example.com/token";
        options.FlowType = OAuth2FlowType.AuthorizationCode;
        options.Scopes = new Dictionary<string, string>
        {
            ["read"] = "Read access",
            ["write"] = "Write access",
            ["admin"] = "Admin access"
        };
    });
```

#### API Key Authentication

```csharp
builder.Services.AddMicroKitOpenApi(builder.Configuration)
    .AddApiKeySecurity(options =>
    {
        options.Name = "X-Api-Key";
        options.Location = ApiKeyLocation.Header;
        options.Description = "API Key for authentication";
    });
```

### Custom Filters

#### Document Filter

```csharp
public class CustomDocumentFilter : IOpenApiDocumentFilter
{
    public void Apply(OpenApiDocument document, DocumentFilterContext context)
    {
        document.Info.Extensions["x-custom"] = JsonValue.Create("value");
    }
}

// Registration
builder.Services.AddMicroKitOpenApi(builder.Configuration)
    .AddDocumentFilter<CustomDocumentFilter>();
```

#### Operation Filter

```csharp
public class CustomOperationFilter : IOpenApiOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        operation.Tags.Add(new OpenApiTag { Name = "Custom" });
    }
}

// Registration
builder.Services.AddMicroKitOpenApi(builder.Configuration)
    .AddOperationFilter<CustomOperationFilter>();
```

#### Schema Filter

```csharp
public class CustomSchemaFilter : IOpenApiSchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type == typeof(DateTime))
        {
            schema.Format = "date-time";
            schema.Example = JsonValue.Create("2025-01-01T00:00:00Z");
        }
    }
}

// Registration
builder.Services.AddMicroKitOpenApi(builder.Configuration)
    .AddSchemaFilter<CustomSchemaFilter>();
```

## Built-in Filters

### DeprecationDocumentFilter

Automatically marks all operations as deprecated for deprecated API versions.

```csharp
builder.Services.AddMicroKitOpenApi(builder.Configuration)
    .AddDocumentFilter<DeprecationDocumentFilter>();
```

### ExamplesOperationFilter

Adds examples from custom attributes to operations.

```csharp
builder.Services.AddMicroKitOpenApi(builder.Configuration)
    .AddOperationFilter<ExamplesOperationFilter>();
```

### RequiredSchemaFilter

Marks properties with `[Required]` attribute as required in the schema.

```csharp
builder.Services.AddMicroKitOpenApi(builder.Configuration)
    .AddSchemaFilter<RequiredSchemaFilter>();
```

## Scalar UI Themes

Available themes:

| Theme | Description |
|-------|-------------|
| `Default` | Default Scalar theme |
| `Alternate` | Alternate light theme |
| `Moon` | Dark moon theme |
| `Purple` | Purple accent theme |
| `Solarized` | Solarized color scheme |
| `BluePlanet` | Blue planet theme |
| `Saturn` | Saturn theme |
| `Kepler` | Kepler theme |
| `Mars` | Mars red theme |
| `DeepSpace` | Deep space dark theme |

```csharp
builder.Services.AddMicroKitOpenApi(builder.Configuration)
    .ConfigureScalar(options =>
    {
        options.Theme = ScalarTheme.DeepSpace;
        options.DarkMode = true;
    });
```

## API Versioning

MicroKit.OpenApi integrates with `Asp.Versioning` for comprehensive API versioning support.

### Version Declaration in Controllers

```csharp
[ApiController]
[ApiVersion("1.0")]
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class ProductsController : ControllerBase
{
    [HttpGet]
    [MapToApiVersion("1.0")]
    public IActionResult GetV1() => Ok(new { Version = "1.0" });

    [HttpGet]
    [MapToApiVersion("2.0")]
    public IActionResult GetV2() => Ok(new { Version = "2.0", Enhanced = true });
}
```

### Version Declaration in Minimal APIs

```csharp
var v1 = app.NewApiVersionSet()
    .HasApiVersion(new ApiVersion(1, 0))
    .Build();

var v2 = app.NewApiVersionSet()
    .HasApiVersion(new ApiVersion(2, 0))
    .Build();

app.MapGet("/api/v{version:apiVersion}/products", () => "Products V1")
    .WithApiVersionSet(v1);

app.MapGet("/api/v{version:apiVersion}/products", () => "Products V2")
    .WithApiVersionSet(v2);
```

## Endpoints

| Endpoint | Description |
|----------|-------------|
| `/openapi/{version}.json` | OpenAPI specification for a specific version |
| `/scalar` | Scalar UI documentation |
| `/scalar/v1` | Scalar UI for version 1.0 |

## Complete Integration Example

### Program.cs

```csharp
using MicroKit.OpenApi.Extensions;
using MicroKit.OpenApi.Configuration;
using MicroKit.OpenApi.Filters;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Configure MicroKit OpenAPI
builder.Services.AddMicroKitOpenApi(builder.Configuration)
    .Configure(options =>
    {
        options.Title = "E-Commerce API";
        options.Description = "REST API for e-commerce platform";
        options.TermsOfServiceUrl = "https://example.com/terms";
    })
    .AddVersion("1.0")
    .AddVersion("2.0")
    .AddVersion("0.9", deprecated: true)
    .AddServer("https://api.example.com", "Production")
    .AddServer("https://localhost:5001", "Development")
    .AddBearerSecurity(bearer =>
    {
        bearer.Description = "Enter 'Bearer {token}'";
    })
    .AddDocumentFilter<DeprecationDocumentFilter>()
    .AddSchemaFilter<RequiredSchemaFilter>()
    .ConfigureScalar(scalar =>
    {
        scalar.Theme = ScalarTheme.Moon;
        scalar.DarkMode = true;
        scalar.ShowSidebar = true;
        scalar.ShowDownloadButton = true;
    });

var app = builder.Build();

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.UseMicroKitOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
```

### appsettings.json

```json
{
  "MicroKitOpenApi": {
    "Title": "E-Commerce API",
    "Description": "REST API for e-commerce platform",
    "DefaultVersion": "2.0",
    "SupportedVersions": ["1.0", "2.0"],
    "DeprecatedVersions": ["0.9"],
    "EnableScalar": true,
    "Theme": "Moon",
    "ScalarEndpointPath": "/docs/{documentName}",
    "OpenApiEndpointPath": "/openapi/{documentName}.json",
    "Contact": {
      "Name": "API Team",
      "Email": "api@example.com"
    },
    "Securities": [
      {
        "Type": "Bearer",
        "SchemeName": "UserAuth",
        "Description": "JWT for End-Users"
      },
      {
        "Type": "ApiKey",
        "SchemeName": "ServiceAuth",
        "Name": "X-Internal-Key",
        "Location": "Header"
      },
      {
        "Type": "OAuth2",
        "SchemeName": "Keycloak",
        "FlowType": "AuthorizationCode",
        "AuthorizationUrl": "https://auth.example.com/authorize",
        "TokenUrl": "https://auth.example.com/token",
        "Scopes": {
          "api:read": "Read access",
          "api:write": "Write access"
        }
      }
    ]
  }
}
```

## Dependencies

| Package | Version |
|---------|---------|
| Microsoft.AspNetCore.OpenApi | 10.0.0 |
| Microsoft.OpenApi | 2.3.0 |
| Asp.Versioning.Http | 9.0.0 |
| Asp.Versioning.Mvc.ApiExplorer | 9.0.0 |
| Scalar.AspNetCore | 2.12.0 |

## Requirements

- .NET 10.0 or later
- ASP.NET Core 10.0 or later

## License

MIT License - see LICENSE file for details.
