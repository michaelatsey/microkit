# Workflow: Adding a Provider

Step-by-step guide for adding a new EF Core provider to MicroKit.Persistence.

## When to Use

When a new database provider needs to be supported (e.g., MySQL, SQLite for tests, Oracle).

## Steps

### 1. Create the Provider Project

```
/new-provider <ProviderName>
```

Name: `MicroKit.Persistence.EntityFrameworkCore.<ProviderName>`

### 2. Set Up Project Dependencies

```xml
<!-- In the provider .csproj -->
<ProjectReference Include="../MicroKit.Persistence.EntityFrameworkCore/MicroKit.Persistence.EntityFrameworkCore.csproj" />
<PackageReference Include="<Provider.EFCore.Package>" />
```

Add the provider package version to `Directory.Packages.props`.

### 3. Implement Provider-Specific DbContext Options

```csharp
public static class EfCoreBuilderExtensions
{
    public static EfCoreBuilder UseMyProvider(
        this EfCoreBuilder builder,
        string connectionString,
        Action<DbContextOptionsBuilder>? configure = null)
    {
        builder.Services.AddDbContext<AppDbContext>(opts =>
        {
            opts.UseMyProvider(connectionString);
            configure?.Invoke(opts);
        });
        return builder;
    }
}
```

### 4. Add Provider-Specific Conventions (if needed)

```csharp
// PostgreSQL snake_case naming
protected override void ConfigureConventions(ModelConfigurationBuilder cfg)
    => cfg.Properties<string>().HaveMaxLength(500);
```

### 5. Invoke Dependency Guardian

Invoke `dependency-guardian` to verify the provider package is correctly confined to the
new project only and does not leak into Core or Abstractions.

### 6. Add Integration Tests

Create a test project `MicroKit.Persistence.IntegrationTests.<ProviderName>` using
Testcontainers for the provider.

### 7. Add CI Workflow

Copy `.github/workflows/ci-persistence.yml` and add a matrix entry for the new provider.
