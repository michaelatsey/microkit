# Contributing to MicroKit.Domain

## Overview

MicroKit.Domain maintains strict architectural boundaries and performance characteristics. This document outlines the contribution process and coding standards that ensure consistency and quality.

## Development Environment

### Prerequisites

- .NET 10 SDK
- C# 12 language features
- Git 2.x+

### AI-Assisted Development

This repository uses structured AI assistance via the `.claude/` directory:

```bash
# Generate new aggregate
/new-aggregate Order --id OrderId --events OrderPlaced,OrderShipped

# Create value object  
/new-value-object ProductCode --fields "value:string" --validate

# Modernize existing patterns
/modernize-value-objects
```

The AI brain is configured with domain-specific knowledge about architectural patterns and performance requirements.

## Architecture Constraints

### Domain Purity Rules

**MUST NOT**:
- Reference infrastructure concerns (databases, HTTP, external APIs)
- Use reflection or runtime code generation
- Include dependencies beyond .NET runtime
- Implement mutable state in value objects
- Use abstract base classes for value semantics

**MUST**:
- Use `sealed record` for value objects
- Use `readonly record struct` for simple identifiers  
- Maintain zero-allocation equality paths
- Follow immutable-first design
- Raise domain events AFTER state mutations

### Performance Requirements

All contributions must maintain or improve allocation profiles:

```csharp
[Benchmark]
[MemoryDiagnoser]
public class ValueObjectBenchmark
{
    [Benchmark]
    public bool EqualityComparison()
    {
        var money1 = new Money(100m, "USD");
        var money2 = new Money(100m, "USD");
        return money1 == money2; // MUST be 0 allocations
    }
}
```

## Code Quality Standards

### Value Objects

```csharp
// ✅ Correct pattern
public sealed record Money(decimal Amount, string Currency) : IValueObject
{
    public Money
    {
        if (Amount < 0) throw new DomainException("Amount cannot be negative");
        ArgumentException.ThrowIfNullOrWhiteSpace(Currency);
    }
}

// ❌ Incorrect pattern - mutable properties
public class Money : ValueObject
{
    public decimal Amount { get; set; } // Violates immutability
}
```

### Aggregates

```csharp
// ✅ Correct pattern - private constructor + static factory
public sealed class Order : AggregateRoot<OrderId>
{
    private Order(OrderId id) : base(id) { }
    
    public static Order Create(CustomerId customerId, IReadOnlyList<OrderItem> items)
    {
        var order = new Order(OrderId.New());
        order.CheckRule(new OrderMustHaveItemsRule(items));
        order.RaiseDomainEvent(new OrderCreatedEvent(order.Id));
        return order;
    }
}
```

### Domain Events

```csharp
// ✅ Correct pattern - sealed record with timestamp
public sealed record OrderShippedEvent(
    OrderId OrderId, 
    TrackingNumber TrackingNumber,
    DateTimeOffset OccurredAt) : DomainEvent;
```

## Testing Requirements

### Test Categories

1. **Unit Tests** - Behavior verification
2. **Architecture Tests** - Constraint enforcement  
3. **Performance Tests** - Allocation validation
4. **Integration Tests** - Cross-boundary scenarios

### Required Test Coverage

```csharp
[Fact]
public void ValueObject_Equality_Should_Be_ZeroAllocation()
{
    // Arrange
    var money1 = new Money(100m, "USD");
    var money2 = new Money(100m, "USD");
    
    // Act & Assert - verify no allocations
    using var activity = AllocationTracker.Start();
    var result = money1 == money2;
    activity.Should().HaveAllocated(0);
}

[Fact]
public void Domain_Should_NotReference_Infrastructure()
{
    Types.InAssembly(typeof(AggregateRoot<>).Assembly)
        .Should()
        .NotHaveDependencyOn("Microsoft.EntityFrameworkCore")
        .AndShould()
        .NotHaveDependencyOn("System.Net.Http");
}
```

## Pull Request Process

### Pre-Submission Checklist

- [ ] All architecture tests pass
- [ ] Performance benchmarks show no regressions
- [ ] New code includes comprehensive test coverage
- [ ] AI commands updated if new patterns introduced
- [ ] Documentation reflects changes

### Build Verification

```bash
# Run full validation suite
dotnet build
dotnet test tests/MicroKit.Domain.UnitTests
dotnet test tests/MicroKit.Domain.ArchitectureTests  
dotnet run --project benchmarks/MicroKit.Domain.Benchmarks -c Release
```

### Review Criteria

**Automatic Acceptance**:
- Improves performance without behavioral changes
- Adds comprehensive test coverage
- Fixes bugs with regression tests

**Requires Discussion**:
- Changes public API surface
- Introduces new abstractions
- Modifies allocation characteristics

**Automatic Rejection**:
- Violates domain purity rules
- Introduces external dependencies  
- Degrades performance without justification
- Lacks adequate test coverage

## Common Contribution Patterns

### Adding New Value Objects

1. Use `/new-value-object` command for scaffolding
2. Implement validation in primary constructor
3. Add comprehensive test suite
4. Verify zero-allocation equality

### Extending Aggregates

1. Maintain private constructors
2. Use static factory methods for creation
3. Raise events AFTER state changes
4. Include business rule validation

### Performance Optimizations

1. Add benchmark demonstrating improvement  
2. Verify no functional regressions
3. Update performance documentation
4. Include allocation analysis

## Documentation Standards

### XML Documentation

All public APIs require comprehensive XML documentation:

```csharp
/// <summary>
/// Represents a monetary amount with currency validation.
/// Provides zero-allocation equality and arithmetic operations.
/// </summary>
/// <param name="Amount">The monetary amount</param>
/// <param name="Currency">The ISO 4217 currency code</param>
/// <example>
/// <code>
/// var price = new Money(29.99m, "USD");
/// var total = price.Add(new Money(5.00m, "USD"));
/// </code>
/// </example>
public sealed record Money(decimal Amount, string Currency) : IValueObject;
```

### Architecture Decision Records

Significant changes require ADR documentation in `docs/architecture/`:

```markdown
# ADR-001: Records-First Value Objects

## Status
Accepted

## Context
Traditional abstract base class patterns generate allocation overhead...

## Decision  
Adopt sealed record declarations for all value objects...

## Consequences
130x performance improvement in equality operations...
```

## Release Process

### Version Strategy

- **Major**: Breaking API changes, architectural shifts
- **Minor**: New features, performance improvements  
- **Patch**: Bug fixes, documentation updates

### Release Checklist

- [ ] All benchmarks validate performance claims
- [ ] Architecture tests pass
- [ ] Documentation reflects changes
- [ ] CHANGELOG.md updated
- [ ] NuGet package metadata current

---

## Questions?

For architectural discussions, performance concerns, or contribution guidance, open an issue with the `contribution` label. The maintainers prioritize clear communication and technical excellence over bureaucratic process.