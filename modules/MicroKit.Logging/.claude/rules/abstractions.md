# Rule: Abstractions

Rules governing the design and evolution of `MicroKit.Logging.Abstractions`.

## Design Constraints

1. **Zero third-party dependencies** — only `Microsoft.Extensions.Logging.Abstractions` is allowed, and only if strictly necessary. Prefer no external dependency at all.
2. **Interfaces over abstract classes** — all contracts are interfaces
3. **No `default` implementations on interfaces** unless adding a non-breaking overload to a stable interface
4. **Immutable records for data contracts** — `sealed record` for value objects passed between layers
5. **No implementation code** — Abstractions must contain zero business logic. Extension methods are allowed if purely additive.
6. **No `internal` types** — everything in Abstractions is `public` by design

## Stability Contract

Once a type is published in `MicroKit.Logging.Abstractions`:
- Removing a member → **BREAKING** → major version bump
- Changing a parameter type → **BREAKING** → major version bump
- Adding a member to an interface → **BREAKING** (unless `default` impl) → major version bump
- Adding a new type → **non-breaking** → minor version bump
- Adding an overload (new method) → **non-breaking** → minor version bump

## Naming

- Interfaces: `I[Noun]` — `ILogEnricher`, `IOperationContext`, `ILogContextAccessor`
- Records (data contracts): descriptive noun — `OperationContextSnapshot`, `LogPropertyEntry`
- Constants classes: `[Domain]Names` or `[Domain]Constants` — `LogPropertyNames`, `LogScopeNames`
- Enums: singular noun — `LoggingScope`, `EnrichmentPhase`

## What Belongs Here

✅ `ILogEnricher` — consumed by modules providing enrichment  
✅ `IOperationContext` — consumed by any module reading the context  
✅ `ILogContextAccessor` — ambient context accessor  
✅ `ILogScopeFactory` — scope creation contract  
✅ `LogPropertyNames` — canonical property name constants  
✅ `LogScopeNames` — canonical scope name constants  
✅ `LogCategoryNames` — canonical category constants  

❌ `EnrichmentPipeline` — implementation, belongs in Core  
❌ `AsyncLocalOperationContext` — implementation detail  
❌ Any class with constructor logic or state
