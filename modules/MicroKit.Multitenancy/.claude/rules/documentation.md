# Rule: Documentation — MicroKit.Multitenancy

## XML Documentation (src/ projects only)

All public types and members in `src/` require XML documentation.
A missing `<summary>` on a public member is a build error in Release configuration.

```xml
<GenerateDocumentationFile>true</GenerateDocumentationFile>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
```

## Minimum Required Tags

| Member type | Required tags |
|-------------|--------------|
| Interface | `<summary>` |
| Method | `<summary>` + `<param>` + `<returns>` |
| Record/property | `<summary>` |
| Exception thrown | `<exception cref="...">` |
| Override | `<inheritdoc/>` |
| Event | `<summary>` |

## Contract Documentation Patterns

```csharp
/// <summary>
/// Resolves the current tenant by iterating registered
/// <see cref="ITenantResolutionStrategy"/> instances in priority order.
/// Short-circuits on the first successful resolution.
/// </summary>
/// <param name="ct">Propagates notification that the operation should be cancelled.</param>
/// <returns>
/// <see cref="Result{ITenantInfo}"/> containing the resolved tenant on success,
/// or a failure result if no strategy could resolve a tenant.
/// </returns>
ValueTask<Result<ITenantInfo>> ResolveAsync(CancellationToken ct = default);
```

```csharp
/// <summary>
/// Sets the current tenant for the active async execution context.
/// </summary>
/// <param name="tenant">The tenant to set, or <see langword="null"/> to clear.</param>
void SetTenant(ITenantInfo? tenant);

/// <summary>
/// Creates a scoped tenant context that restores the previous tenant on disposal.
/// Use this for background work and parallel tasks that require a specific tenant context.
/// </summary>
/// <param name="tenant">The tenant to activate for the scope duration.</param>
/// <returns>An <see cref="IDisposable"/> that restores the previous tenant when disposed.</returns>
IDisposable CreateScope(ITenantInfo tenant);
```

## Style

- `<summary>` uses imperative mood: "Resolves the current tenant." not "This method resolves..."
- Cross-reference related contracts with `<see cref=""/>`
- Document the host-agnostic nature of Core types (not bound to HTTP)
- Override implementations use `<inheritdoc/>` exclusively — no duplication
