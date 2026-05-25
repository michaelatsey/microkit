# Template: Module

Skeleton template for a new MicroKit module that integrates with MicroKit.Logging.

This template is for **other MicroKit modules** that want to provide log enrichment — not for new projects within MicroKit.Logging itself.

---

## Dependency Declaration

In the new module's `MicroKit.{Module}.Abstractions.csproj`:

```xml
<ItemGroup>
  <!-- Logging integration — Abstractions only, no concrete dependency -->
  <ProjectReference Include="..\..\..\MicroKit.Logging\src\MicroKit.Logging.Abstractions\MicroKit.Logging.Abstractions.csproj" />
</ItemGroup>
```

---

## Enricher Implementation

```csharp
// In MicroKit.{Module} (or MicroKit.{Module}.Abstractions if context is a contract)
using MicroKit.Logging.Abstractions;
using MicroKit.Logging.Abstractions.Enrichment;

namespace MicroKit.{Module}.Logging;

/// <summary>
/// Enriches log entries with {Module} context — {property list}.
/// </summary>
public sealed class {Module}LogEnricher : ILogEnricher
{
    private readonly I{Module}ContextAccessor _contextAccessor;

    /// <summary>
    /// Initializes a new instance of <see cref="{Module}LogEnricher"/>.
    /// </summary>
    public {Module}LogEnricher(I{Module}ContextAccessor contextAccessor)
    {
        _contextAccessor = contextAccessor;
    }

    /// <inheritdoc />
    public void Enrich(IEnrichmentContext context)
    {
        if (!context.IsEnabled) return;

        var current = _contextAccessor.Current;
        if (current is null) return;

        // Use ONLY LogPropertyNames constants
        context.Properties[LogPropertyNames.{PropertyName}] = current.{PropertyValue};
    }
}
```

---

## DI Registration

```csharp
public static IServiceCollection Add{Module}(
    this IServiceCollection services,
    Action<{Module}Options>? configure = null)
{
    // ... module registration ...

    // Logging enricher — registered automatically when module is added
    services.AddSingleton<ILogEnricher, {Module}LogEnricher>();

    return services;
}
```

---

## Integration Notes

- The enricher is registered as `ILogEnricher` in DI — MicroKit.Logging's pipeline picks it up automatically
- Only `MicroKit.Logging.Abstractions` is referenced — no coupling to the concrete pipeline
- The new property added by this module must be registered in `.claude-context/standards/log-properties.md` first
