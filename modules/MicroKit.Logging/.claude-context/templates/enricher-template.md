# Template: Enricher

Code template for a new `ILogEnricher` implementation.

Used by `/new-enricher` command. Replace all `{Placeholder}` values.

---

## File: `{EnricherName}LogEnricher.cs`

```csharp
// Target project: MicroKit.Logging.{Project}
// Namespace: MicroKit.Logging.{Project}.Enrichers

using Microsoft.Extensions.Logging;
using MicroKit.Logging.Abstractions;
using MicroKit.Logging.Abstractions.Enrichment;

namespace MicroKit.Logging.{Project}.Enrichers;

/// <summary>
/// Enriches log entries with {description of what this enricher adds}.
/// </summary>
public sealed class {EnricherName}LogEnricher : ILogEnricher
{
    private readonly I{ContextAccessor} _contextAccessor;

    /// <summary>
    /// Initializes a new instance of <see cref="{EnricherName}LogEnricher"/>.
    /// </summary>
    /// <param name="contextAccessor">Provides access to the current {context} context.</param>
    public {EnricherName}LogEnricher(I{ContextAccessor} contextAccessor)
    {
        _contextAccessor = contextAccessor;
    }

    /// <inheritdoc />
    public void Enrich(IEnrichmentContext context)
    {
        // Guard: do not compute properties if log level is not active
        if (!context.IsEnabled)
        {
            return;
        }

        var current = _contextAccessor.Current;
        if (current is null)
        {
            return;
        }

        // Use ONLY LogPropertyNames constants — never hardcoded strings
        context.Properties[LogPropertyNames.{PropertyName}] = current.{PropertyValue};
        // Add additional properties as needed, each on its own line
    }
}
```

---

## File: `{EnricherName}LogEnricherTests.cs`

```csharp
// Target project: MicroKit.Logging.UnitTests
// Namespace: MicroKit.Logging.UnitTests.Enrichers

using FluentAssertions;
using MicroKit.Logging.Abstractions;
using MicroKit.Logging.{Project}.Enrichers;
using NSubstitute;
using Xunit;

namespace MicroKit.Logging.UnitTests.Enrichers;

public sealed class {EnricherName}LogEnricherTests
{
    private readonly I{ContextAccessor} _contextAccessor = Substitute.For<I{ContextAccessor}>();
    private readonly {EnricherName}LogEnricher _sut;

    public {EnricherName}LogEnricherTests()
    {
        _sut = new {EnricherName}LogEnricher(_contextAccessor);
    }

    [Fact]
    public void Enrich_WhenContextIsAvailable_Adds{PropertyName}()
    {
        // Arrange
        var context = Substitute.For<IEnrichmentContext>();
        context.IsEnabled.Returns(true);
        var properties = new Dictionary<string, object?>();
        context.Properties.Returns(properties);
        _contextAccessor.Current.Returns(new { {PropertyValue} = "test-value" });

        // Act
        _sut.Enrich(context);

        // Assert
        properties.Should().ContainKey(LogPropertyNames.{PropertyName});
        properties[LogPropertyNames.{PropertyName}].Should().Be("test-value");
    }

    [Fact]
    public void Enrich_WhenContextIsNull_DoesNotAddProperties()
    {
        // Arrange
        var context = Substitute.For<IEnrichmentContext>();
        context.IsEnabled.Returns(true);
        var properties = new Dictionary<string, object?>();
        context.Properties.Returns(properties);
        _contextAccessor.Current.Returns((object?)null);

        // Act
        _sut.Enrich(context);

        // Assert
        properties.Should().BeEmpty();
    }

    [Fact]
    public void Enrich_WhenLogLevelDisabled_DoesNotAccessContext()
    {
        // Arrange
        var context = Substitute.For<IEnrichmentContext>();
        context.IsEnabled.Returns(false);

        // Act
        _sut.Enrich(context);

        // Assert — context accessor must not be called on disabled level
        _ = _contextAccessor.DidNotReceive().Current;
    }

    [Fact]
    public void Enrich_UsesCanonicalPropertyName()
    {
        // This test documents the canonical property name contract
        // If it breaks, it means LogPropertyNames was changed without updating the enricher
        var context = Substitute.For<IEnrichmentContext>();
        context.IsEnabled.Returns(true);
        var properties = new Dictionary<string, object?>();
        context.Properties.Returns(properties);
        _contextAccessor.Current.Returns(new { {PropertyValue} = "value" });

        _sut.Enrich(context);

        // Canonical name check — must match LogPropertyNames exactly
        properties.Should().ContainKey(LogPropertyNames.{PropertyName},
            because: $"enricher must use LogPropertyNames.{PropertyName} = \"{LogPropertyNames.{PropertyName}}\"");
    }
}
```

---

## DI Registration (in target project's extension)

```csharp
public static ILoggingBuilder AddMicroKit{Project}(this ILoggingBuilder builder)
{
    builder.Services.AddSingleton<ILogEnricher, {EnricherName}LogEnricher>();
    return builder;
}
```
