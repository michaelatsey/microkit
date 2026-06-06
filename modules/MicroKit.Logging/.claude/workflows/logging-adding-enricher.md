# Workflow: Logging Adding an Enricher

Step-by-step guide for adding a new `ILogEnricher` to MicroKit.Logging.

## When to Use

When a new category of contextual information should be automatically added to log entries — for example, adding HTTP request properties, CQRS operation metadata, or tenant information.

## Decision: Where Does It Live?

| Enricher type | Target project |
|--------------|---------------|
| Core context (OperationId, CorrelationId) | `MicroKit.Logging` |
| HTTP request data | `MicroKit.Logging.AspNetCore` |
| Provider-specific metadata | `MicroKit.Logging.<Provider>` |
| Module-specific (CQRS, Tenancy) | That module's project, depends on `Abstractions` |

## Steps

### 1. Verify the Property Name

Check `.claude-context/standards/log-properties.md` — the property you want to add must be listed there.

If not listed: add it to the standards first, get it reviewed, then proceed.

### 2. Use the Command

```
/new-enricher <EnricherName> [--project <ProjectName>]
```

### 3. Implement

Load `.claude-context/templates/logging-enricher-template.md`.

Critical implementation rules:
- `sealed` class
- Guard with `logger.IsEnabled()` before property computation
- Use `LogPropertyNames.<PropertyName>` — never a hardcoded string
- Zero allocation on the "nothing to enrich" path
- lightweight
- deterministic
- allocation-conscious
- provider-agnostic
- no hidden network calls
- no database calls
- no blocking operations
- no service locator patterns

### 4. Checklist

- [ ] No provider dependency
- [ ] No reflection
- [ ] No serialization
- [ ] Structured metadata only
- [ ] Benchmarked if hot path
- [ ] Architecture tests added
- [ ] XML docs added
- [ ] Samples added

### 5. Register in DI

```csharp
// In the target project's DI extension
builder.Services.AddSingleton<ILogEnricher, TenantLogEnricher>();
```

### 6. Write Tests

Use `/logging-generate-tests <EnricherName>`.

Tests must verify:
- Canonical property name used (compare against `LogPropertyNames.*`)
- Property set when context is available
- No property set (no exception) when context is null
- Allocation test: zero bytes allocated on no-op path

### 7. Performance Review

Run: `/logging-review-performance --file src/.../Enrichers/<EnricherName>LogEnricher.cs`

