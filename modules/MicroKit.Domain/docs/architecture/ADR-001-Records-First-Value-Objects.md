# ADR-001: Records-First Value Objects

## Status
**Accepted** - Implemented in MicroKit.Domain v1.0

## Context

Traditional Domain-Driven Design implementations rely on abstract base classes for value object equality:

```csharp
public abstract class ValueObject
{
    protected abstract IEnumerable<object> GetAtomicValues();
    
    public override bool Equals(object obj)
    {
        // LINQ-based comparison with allocations
        return GetAtomicValues().SequenceEqual(other.GetAtomicValues());
    }
}
```

This pattern has significant performance implications:
- **Allocation overhead**: Each equality check allocates enumerators and potentially boxes value types
- **Virtual dispatch**: Method calls through abstract base classes prevent JIT optimizations
- **LINQ dependency**: SequenceEqual and related operations add complexity and allocations
- **Poor hashing**: Aggregate-based hash code generation is collision-prone

Modern C# (10+) provides `record` declarations with compiler-generated structural equality that outperforms manual implementations by orders of magnitude.

## Decision

**Adopt `sealed record` declarations for all value objects in MicroKit.Domain.**

### Implementation Strategy

```csharp
// ✅ NEW: Zero-allocation structural equality
public sealed record Money(decimal Amount, string Currency) : IValueObject
{
    public Money // Primary constructor validation
    {
        if (Amount < 0) throw new DomainException("Amount cannot be negative");
        ArgumentException.ThrowIfNullOrWhiteSpace(Currency);
    }
    
    public Money Add(Money other) =>
        Currency == other.Currency 
            ? new(Amount + other.Amount, Currency)
            : throw new DomainException("Currency mismatch");
}

// ✅ Simple identifiers as value types
public readonly record struct ProductId(Guid Value) : IEntityId
{
    public static ProductId New() => new(Guid.NewGuid());
}
```

### Migration Rules

1. **Complex Value Objects** → `sealed record`
   - Multiple properties
   - Business logic methods
   - Validation requirements
   - String properties

2. **Simple Identifiers** → `readonly record struct`
   - Single primitive property
   - ≤16 bytes total size
   - High-frequency usage
   - No null semantics needed

3. **Marker Interface** → `IValueObject`
   - Generic constraints when needed
   - Repository type parameters
   - Framework integration points

## Consequences

### Performance Improvements

| Metric | Abstract ValueObject | sealed record | Improvement |
|--------|---------------------|---------------|-------------|
| **Equality Check** | 157ns, 156 bytes | 1.2ns, 0 bytes | **130x faster** |
| **Hash Code** | 89ns, 96 bytes | 0.8ns, 0 bytes | **110x faster** |
| **Object Creation** | 45ns, 48 bytes | 4.3ns, 32 bytes | **11x faster** |

### Developer Experience

**Positive**:
- 90% less boilerplate code
- Native IntelliSense and debugging support
- Compiler-enforced immutability
- Pattern matching and deconstruction support
- `with` expressions for transformation

**Neutral**:
- Learning curve for teams accustomed to inheritance-based patterns
- Requires understanding of when to use `record` vs `readonly record struct`

### Runtime Characteristics

**Positive**:
- **NativeAOT compatible**: No reflection or virtual dispatch
- **Trimming safe**: Static analysis can eliminate unused code paths
- **JIT friendly**: Inlining and devirtualization opportunities
- **Serialization optimized**: System.Text.Json has native record support

**Considerations**:
- Records generate more metadata than minimal classes (negligible impact)
- ToString() behavior is different (generally improved)

### API Compatibility

**Breaking Changes**:
- Value objects no longer inherit from common base class
- Equality semantics remain identical (structural equality preserved)
- ToString() format may differ (generally more readable)

**Migration Path**:
```csharp
// Old pattern users can adopt gradually
public interface IValueObject { } // Marker interface for constraints

// Existing aggregates continue working unchanged
public class Order : AggregateRoot<OrderId> 
{
    public Money TotalAmount { get; private set; } // Works with new Money record
}
```

## Alternatives Considered

### 1. Modernized Abstract Base Class

**Approach**: Optimize existing ValueObject class with Span-based comparisons
```csharp
public abstract class ValueObject 
{
    protected abstract ReadOnlySpan<object> GetEqualityComponents();
    // Optimized implementation...
}
```

**Rejected Because**:
- Still requires virtual dispatch overhead
- Boxing value types into object arrays
- More complex than compiler-generated equivalents
- Maintains unnecessary abstraction layer

### 2. Source Generator Approach

**Approach**: Generate equality methods via source generators
```csharp
[ValueObject]
public partial class Money
{
    public decimal Amount { get; }
    public string Currency { get; }
}
```

**Rejected Because**:
- Additional complexity and tooling dependency
- Records provide equivalent functionality natively
- Source generators add compilation overhead
- Debugging generated code is more difficult

### 3. Struct-Only Strategy

**Approach**: Use only `readonly struct` for all value objects
```csharp
public readonly struct Money
{
    public decimal Amount { get; }
    public string Currency { get; } // ❌ Reference in struct
}
```

**Rejected Because**:
- Structs with reference fields create copying overhead
- Large structs (>16 bytes) have poor performance characteristics
- No inheritance support for shared interfaces
- Null semantics complications

## Implementation Notes

### Validation Patterns

```csharp
// ✅ Primary constructor validation (C# 12+)
public sealed record Email(string Value) : IValueObject
{
    public Email
    {
        if (string.IsNullOrWhiteSpace(Value))
            throw new DomainException("Email cannot be empty");
        // Additional validation...
    }
}

// ✅ Property-based validation (C# 10+)
public sealed record Money(decimal Amount, string Currency) : IValueObject
{
    public decimal Amount { get; } = Amount >= 0 
        ? Amount 
        : throw new DomainException("Amount cannot be negative");
    
    public string Currency { get; } = !string.IsNullOrEmpty(Currency)
        ? Currency.ToUpperInvariant()
        : throw new DomainException("Currency is required");
}
```

### Business Logic Integration

```csharp
public sealed record Money(decimal Amount, string Currency) : IValueObject
{
    // Immutable operations return new instances
    public Money Add(Money other)
    {
        if (Currency != other.Currency)
            throw new DomainException($"Cannot add {Currency} to {other.Currency}");
        
        return new Money(Amount + other.Amount, Currency);
    }
    
    // Static factory methods for common scenarios
    public static Money Zero(string currency) => new(0m, currency);
    public static Money FromCents(long cents, string currency) => new(cents / 100m, currency);
    
    // Operators for natural usage
    public static Money operator +(Money left, Money right) => left.Add(right);
}
```

## References

- [C# 9 Records Documentation](https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/record)
- [Performance Analysis: Records vs Classes](./performance-analysis-records-vs-classes.md)
- [.NET 10 NativeAOT Compatibility Guidelines](https://docs.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
- [BenchmarkDotNet Results](../performance/ValueObject-Benchmarks.md)

---

**Last Updated**: May 2026  
**Next Review**: December 2026  
**Author**: MicroKit.Domain Architecture Team