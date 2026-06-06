# Rule: Analyzers — MicroKit.Multitenancy

## Always active for any task touching `MicroKit.Multitenancy.Analyzers`.

## Diagnostic Registry

All 3 diagnostics share the `MKT` prefix (MicroKit Tenancy).

| ID | Severity | Category | Title | Analyzer class |
|----|----------|----------|-------|----------------|
| `MKT001` | Error | Design | Entity implementing ITenantEntity without TenantId property | `TenantEntityAnalyzer` |
| `MKT002` | Warning | Usage | IgnoreQueryFilters() called without justification comment | `QueryFilterBypassAnalyzer` |
| `MKT003` | Error | Usage | ITenantContextAccessor injected in a singleton service | `SingletonTenantAccessorAnalyzer` |

## Detection Strategies

### MKT001 — `TenantEntityAnalyzer`

- `RegisterSymbolAction(SymbolKind.NamedType)`.
- For each class/struct implementing `ITenantEntity`, verify a non-nullable `TenantId` property
  exists (either declared or inherited). Reports MKT001 at the type name if missing or nullable.
- Null-guard: if `ITenantEntity` symbol is null (package not referenced), skip silently.

### MKT002 — `QueryFilterBypassAnalyzer`

- `RegisterOperationAction(OperationKind.Invocation)`.
- Detects calls to `IgnoreQueryFilters()` on any `IQueryable<T>`.
- Checks the leading trivia of the invocation for a comment matching `// [MTK-BYPASS]`.
- If no such comment exists, reports MKT002 at the method name.
- **Heuristic:** comment must be on the same logical line or the immediately preceding line.

### MKT003 — `SingletonTenantAccessorAnalyzer`

- `RegisterSymbolAction(SymbolKind.NamedType)`.
- Checks every class registered as a Singleton via `AddSingleton<T>()` or
  `AddSingleton(typeof(T))` patterns in DI extension methods or Startup.
- If a Singleton's constructor accepts `ITenantContextAccessor`, reports MKT003.
- **Alternative axis:** `RegisterOperationAction(OperationKind.Invocation)` — detects
  `.AddSingleton<T, TImpl>()` where `TImpl` has `ITenantContextAccessor` in constructor.

## Null-Guard Convention

All helper methods null-guard symbol resolution. If `GetTypeByMetadataName` returns `null`
(target package not referenced), return `false` immediately. Never assume a type symbol is non-null.

## Project Structure

```
src/MicroKit.Multitenancy.Analyzers/
├── MicroKit.Multitenancy.Analyzers.csproj  ← netstandard2.0; analyzers/dotnet/cs/
├── GlobalUsings.cs
├── DiagnosticCategories.cs                 ← Usage + Design
├── Helpers/
│   └── MultitenancySymbolHelper.cs         ← symbol resolution + null-guards
├── TenantEntityAnalyzer.cs                 ← MKT001
├── QueryFilterBypassAnalyzer.cs            ← MKT002
└── SingletonTenantAccessorAnalyzer.cs      ← MKT003
```

## Consuming Projects

```xml
<PackageReference Include="MicroKit.Multitenancy.Analyzers">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
```

## Testing Rules

- **Shouldly** mandatory for auxiliary assertions.
- Framework: `CSharpAnalyzerTest<TAnalyzer, CompatXUnitVerifier>`.
- Inline diagnostic markers: `{|MKTxxx:code|}`.
- Every diagnostic must have at minimum one positive and one negative test.

## Adding a New Diagnostic

1. Assign next ID (`MKT004`, …).
2. Category: `Usage` (behavioral) or `Design` (structural).
3. Severity: `Error` (definite) or `Warning` (heuristic).
4. Add `DiagnosticDescriptor`, update `SupportedDiagnostics`.
5. Update this registry.
6. Write positive + negative tests.
