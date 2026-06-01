# /new-provider

Scaffold a new optional integration/provider project for MicroKit.MediatR
(e.g., `MicroKit.MediatR.Autofac`, `MicroKit.MediatR.Caching.Redis`).

## Usage

```
/new-provider <ProviderName>
```

**Examples:**
```
/new-provider Autofac
/new-provider OpenTelemetry
/new-provider Caching.Redis
```

## What This Command Does

1. Creates `src/MicroKit.MediatR.<ProviderName>/` with the standard project structure
2. Generates the `.csproj` with correct project references (no inline versions)
3. Creates the DI registration extension class (`AddMicroKitMediatR<ProviderName>()` on `IServiceCollection` or a fluent extension on `MediatRBuilder`)
4. Creates the bridge/adapter class skeleton
5. Creates `tests/MicroKit.MediatR.<ProviderName>.IntegrationTests/` skeleton (Shouldly + NSubstitute)
6. Adds both projects to `MicroKit.MediatR.slnx`
7. Runs the `dependency-guardian` agent to validate references

## Scaffold Template

Load `.claude-context/templates/provider-template.md` for the exact code structure.

## Steps

```
1. Confirm provider name follows PascalCase (dots allowed for sub-scopes, no hyphens)
2. Verify MicroKit.MediatR.slnx exists
3. Create src/MicroKit.MediatR.{ProviderName}/ directory
4. Scaffold .csproj — reference template, NO Version= attributes
5. Scaffold registration extension (entry point)
6. Scaffold the bridge/adapter class
7. Add XML docs on all public members
8. Create tests/ skeleton with one smoke test (Shouldly)
9. Add both projects to .slnx
10. Run: Use agent dependency-guardian to validate
```

## Constraints

- The new project may reference `MicroKit.MediatR.Abstractions` and `MicroKit.MediatR` core only — never another provider
- A provider that wraps a behavior concern (cache backend, resilience) references `MicroKit.MediatR.Behaviors`; a pure DI/container adapter references core only
- The new project name must be `MicroKit.MediatR.<ProviderName>` exactly
- All `PackageReference` versions go into `build/Directory.Packages.props`
- No `FluentAssertions` in the integration test project — Shouldly only
