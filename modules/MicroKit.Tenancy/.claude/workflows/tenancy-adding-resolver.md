# Workflow: Multitenancy Adding a Tenant Resolution Strategy

## When to use
Adding a new `ITenantResolutionStrategy` to the AspNetCore or Core package.

## Steps

### 1. Use the tenancy-implementer agent
```
@implementer Add a {source} tenant resolution strategy
```
The implementer reads:
- `.claude/rules/tenancy-resolution-pipeline.md`
- `.claude/rules/tenancy-architecture.md`
- `.claude/rules/tenancy-naming.md`

### 2. Validate the plan
- Strategy returns `Result<TenantId>` (never throws)
- Order value is unique in typical registration order
- Strategy lives in correct package (HTTP-specific → AspNetCore, generic → Core)
- Constructor injection only (no service locator)

### 3. Implement
Using `/new-tenant-resolver` command or manually following the template.

### 4. Register in DI
```csharp
// In MultitenancyBuilder.AddAspNetCoreResolution()
services.AddScoped<ITenantResolutionStrategy, {Name}TenantResolutionStrategy>();
```

### 5. Test
```bash
dotnet test modules/MicroKit.Tenancy/tests/MicroKit.Tenancy.UnitTests -c Release \
  --filter "DisplayName~{Name}TenantResolutionStrategy"
```

### 6. Review (for Abstractions changes only)
If the strategy adds to the public API: `@api-reviewer` review required before merge.

## Rules verified
- resolution-pipeline.md: Result<T> return, no-throw contract
- tenancy-naming.md: {Source}TenantResolutionStrategy convention
- tenancy-dependencies.md: HTTP-specific strategies stay in AspNetCore package
