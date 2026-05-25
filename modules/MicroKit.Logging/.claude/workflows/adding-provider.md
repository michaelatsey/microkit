# Workflow: Adding a Provider

Step-by-step guide for adding a new logging provider integration (e.g., `MicroKit.Logging.Datadog`).

## When to Use

When a consumer ecosystem requires a dedicated integration package that bridges MicroKit.Logging to a specific logging backend or sink, and the integration is substantial enough to warrant its own package (not just a consumer-side configuration).

## Steps

### 1. Define the Provider Scope

Before writing code, answer:
- What does this provider bridge? (MicroKit → Sink X)
- Is it a sink adapter, an enricher, or both?
- What is the minimum viable surface (avoid over-engineering)?

### 2. Use the Command

```
/new-provider <ProviderName>
```

This scaffolds the project structure. Review the generated files before proceeding.

### 3. Implement the Bridge

Load `.claude-context/templates/provider-template.md` for the exact code structure.

Key implementation points:
- DI entry point: `AddMicroKit<ProviderName>()` on `ILoggingBuilder`
- Bridge class: implements `ILogEnricher` or wraps the sink SDK
- No logic in the DI extension — delegate to the bridge class
- `sealed` on all implementation classes

### 4. Validate Dependencies

Run: Use agent dependency-guardian

The provider may only reference:
- `MicroKit.Logging.Abstractions`
- `MicroKit.Logging` core
- The provider's own SDK

### 5. Write Integration Tests

At minimum:
- Provider registers correctly via DI
- Enrichment properties appear in the sink output
- No exception when context is empty

### 6. Update Documentation

- Add to `docs/guides/` a usage guide for the new provider
- Update `README.md` provider table
- Update `.claude-context/context/module-responsibilities.md`

### 7. Architecture Review

Run: `/review-architecture --scope <ProviderName>`
