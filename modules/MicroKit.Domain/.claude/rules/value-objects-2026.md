# Rule: Value Objects 2026 — Modern .NET Strategy

## Always active for all value object implementations.

## Core Principle
> Value objects in 2026 are **records, not classes**.
> Zero inheritance. Zero abstractions. Maximum performance.

## 2026 Architecture Decision

### ✅ RECOMMENDED: Records-Only Strategy
```csharp
// ✅ Complex value objects = sealed record
public sealed record Money(decimal Amount, string Currency)
{
    // Validation in constructor
    public Money
    {
        if (Amount < 0) throw new DomainException("Amount cannot be negative");
        ArgumentException.ThrowIfNullOrWhiteSpace(Currency);
    }
    
    // Business methods
    public Money Add(Money other) => /* implementation */;
}

// ✅ Simple value objects = readonly record struct (when < 16 bytes)
public readonly record struct Percentage(decimal Value)
{
    public Percentage
    {
        if (Value < 0 || Value > 100) 
            throw new DomainException("Percentage must be 0-100");
    }
}
```

### ❌ OBSOLETE: Abstract Base Class
```csharp
// ❌ Don't use - outdated 2016 pattern
public abstract class ValueObject : IEquatable<ValueObject>
{
    protected abstract IEnumerable<object?> GetEqualityComponents();
}
```

## Why Records Win in 2026

### Performance Benefits
- **Zero allocation equality**: Compiler-generated, optimized
- **Zero boxing**: Direct field comparisons
- **Zero virtual dispatch**: Sealed types, direct calls
- **Optimal hashing**: Compiler-generated HashCode.Combine
- **Stack allocation**: Structs avoid heap allocation

### Developer Experience
- **Less boilerplate**: No GetEqualityComponents method
- **Better IntelliSense**: Tooling understands records natively
- **Better debugging**: Native debugger support
- **Immutability by default**: `init` properties

### Runtime Compatibility
- **AOT friendly**: No reflection, no virtual calls
- **Trimming safe**: No dynamic behavior
- **Serialization optimized**: System.Text.Json optimizations
- **JIT optimizations**: Inlining, devirtualization

## Implementation Guidelines

### When to use `sealed record` (reference type)
- Complex value objects with multiple fields
- Value objects with business methods
- Value objects that may grow in the future
- Any value object > 16 bytes

### When to use `readonly record struct` (value type)
- Simple value objects (1-2 primitive fields)
- Frequently used in collections
- Value objects ≤ 16 bytes total
- Performance-critical scenarios

### Size Guidelines
```csharp
// ✅ readonly record struct - 8 bytes
public readonly record struct UserId(Guid Value) : IEntityId;

// ✅ readonly record struct - 8 bytes
public readonly record struct Percentage(decimal Value);

// ✅ sealed record - multiple fields, business logic
public sealed record Money(decimal Amount, string Currency);

// ✅ sealed record - complex behavior
public sealed record Address(string Street, string City, string PostalCode, string Country);
```

## Equality and Hashing

### Let the compiler handle it
```csharp
// ✅ Compiler generates optimal equality
public sealed record Money(decimal Amount, string Currency);

// Result: Perfect structural equality with zero allocations
var m1 = new Money(100, "USD");
var m2 = new Money(100, "USD");
Console.WriteLine(m1 == m2); // True - zero allocations
```

### Custom equality only when needed
```csharp
// ✅ Override only for case-insensitive scenarios
public sealed record Email(string Value)
{
    public string Value { get; } = value.ToLowerInvariant();
    
    // Compiler handles the rest - no custom Equals needed
}
```

## Marker Interface Strategy

### Optional IValueObject for generic constraints
```csharp
// ✅ Minimal marker interface if needed
public interface IValueObject
{
    // Empty - just for type constraints
}

// ✅ Usage
public sealed record Money(decimal Amount, string Currency) : IValueObject;

// ✅ Generic constraints when needed
public class Repository<T> where T : IValueObject
{
    // Can work with any value object
}
```

## Anti-Patterns to Avoid

### ❌ Don't create base classes
```csharp
// ❌ Unnecessary abstraction
public abstract class ValueObject<T> { }

// ✅ Just use records directly
public sealed record Money(decimal Amount, string Currency);
```

### ❌ Don't implement custom equality for records
```csharp
// ❌ Defeats the purpose of records
public sealed record Money(decimal Amount, string Currency)
{
    public override bool Equals(object? obj) => /* custom logic */; // ❌
}
```

### ❌ Don't use mutable properties
```csharp
// ❌ Violates value object immutability
public sealed record Money(decimal Amount, string Currency)
{
    public decimal Amount { get; set; } // ❌
}
```

## Migration Strategy

1. **Remove unused abstract ValueObject class**
2. **Convert any inheritance to composition**
3. **Optimize small value objects to readonly record struct**
4. **Add marker interface only if needed for constraints**
5. **Update documentation to reflect records-first approach**

## Folder Structure
```
ValueObjects/
├── IValueObject.cs          ← marker interface (optional)
└── Common/
    ├── Money.cs             ← sealed record
    ├── Email.cs             ← sealed record  
    ├── Address.cs           ← sealed record
    ├── DateRange.cs         ← readonly record struct
    └── Percentage.cs        ← readonly record struct
```