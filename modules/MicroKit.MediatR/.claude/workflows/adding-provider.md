# Workflow: Adding a Provider

Step-by-step guide for adding a new optional integration package (e.g.,
`MicroKit.MediatR.Autofac`, `MicroKit.MediatR.Caching.Redis`).

## When to Use

When an integration bridges MicroKit.MediatR to a specific container, cache backend, or
observability stack, and it is substantial enough to warrant its own package (not just
consumer-side configuration).

## Steps

### 1. Define the Provider Scope

Before writing code, answer:
- What does this provider bridge? (MicroKit.MediatR → X)
- Is it a DI/container adapter, a behavior backend, or both?
- What is the minimum viable surface (avoid over-engineering)?

### 2. Use the Command

```
/new-provider <ProviderName>
```

This scaffolds the project structure. Review the generated files before proceeding.

### 3. Implement the Bridge

Load `.claude-context/templates/provider-template.md` for the exact code structure.

- DI entry point: `AddMicroKitMediatR<ProviderName>()` (or a fluent extension on `MediatRBuilder`)
- No logic in the DI extension — delegate to the adapter class
- `sealed` on all implementation classes
- A pure container adapter references core only; a behavior backend references `MicroKit.MediatR.Behaviors`

### 4. Validate Dependencies

Run: Use agent dependency-guardian

The provider may only reference:
- `MicroKit.MediatR.Abstractions`
- `MicroKit.MediatR` core (and `Behaviors` only if it backs a behavior)
- The provider's own SDK

Never another provider.

### 5. Write Integration Tests

At minimum (Shouldly + NSubstitute):
- Provider registers correctly via DI
- The integration behaves as expected end-to-end
- No exception on the empty/default path

### 6. Update Documentation

- Add a usage guide under `docs/guides/`
- Update `README.md` provider table
- Update `.claude-context/context/dependency-graph.md`

### 7. Architecture Review

Run: `/review-architecture --scope <ProviderName>`
