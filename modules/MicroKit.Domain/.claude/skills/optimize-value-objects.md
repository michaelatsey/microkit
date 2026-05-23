# Skill: Optimize Value Objects

## Automatically modernizes value objects to 2026 best practices.

### What this skill does:
- Removes unused abstract ValueObject base classes
- Converts appropriate records to readonly record struct for performance
- Adds optimal validation patterns
- Ensures immutability and thread safety
- Optimizes for allocation-conscious design

### When to use:
- Creating new value objects
- Optimizing existing value objects
- Reviewing value object performance
- Modernizing legacy DDD implementations

## Implementation Strategy

### Step 1: Remove Legacy Base Classes
```csharp
// ❌ Remove this pattern
public abstract class ValueObject : IEquatable<ValueObject>
{
    protected abstract IEnumerable<object?> GetEqualityComponents();
}

// ✅ Replace with direct records
public sealed record Money(decimal Amount, string Currency);
```

### Step 2: Choose Optimal Record Type

#### Use `sealed record` when:
- Multiple fields (> 2)
- Complex business logic
- String properties
- May evolve over time
- Size > 16 bytes

```csharp
// ✅ sealed record - complex value object
public sealed record Address(string Street, string City, string PostalCode, string Country)
{
    // Constructor validation
    public Address
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Street);
        ArgumentException.ThrowIfNullOrWhiteSpace(City);
        // ...
    }
    
    // Business methods
    public Address WithCity(string newCity) => this with { City = newCity };
}
```

#### Use `readonly record struct` when:
- Simple value (1-2 primitive fields)
- Size ≤ 16 bytes
- High-frequency usage
- Performance critical

```csharp
// ✅ readonly record struct - simple, performance-critical
public readonly record struct Percentage(decimal Value) : IValueObject
{
    public Percentage
    {
        if (Value < 0 || Value > 100)
            throw new DomainException("Percentage must be between 0 and 100");
    }
    
    public decimal AsFraction => Value / 100m;
}
```

### Step 3: Implement Validation

#### Constructor validation pattern:
```csharp
public sealed record Email(string Value)
{
    // Validate and normalize in property initializer
    public string Value { get; } = ValidateAndNormalize(Value);
    
    private static string ValidateAndNormalize(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new DomainException("Email cannot be null or empty");
        
        // Validation logic...
        return email.ToLowerInvariant();
    }
}
```

#### Primary constructor validation (C# 12+):
```csharp
public sealed record Money(decimal Amount, string Currency)
{
    public Money // Primary constructor validation
    {
        if (Amount < 0) throw new DomainException("Amount cannot be negative");
        ArgumentException.ThrowIfNullOrWhiteSpace(Currency);
    }
}
```

### Step 4: Add Business Methods

```csharp
public sealed record Money(decimal Amount, string Currency)
{
    // Zero-allocation static factories
    public static Money Zero(string currency) => new(0m, currency);
    public static Money FromCents(long cents, string currency) => new(cents / 100m, currency);
    
    // Immutable operations
    public Money Add(Money other)
    {
        ValidateSameCurrency(other);
        return new(Amount + other.Amount, Currency);
    }
    
    // Operators for ergonomics
    public static Money operator +(Money left, Money right) => left.Add(right);
}
```

### Step 5: Optimization Checks

#### Memory layout analysis:
```csharp
// ✅ Good for readonly record struct (16 bytes)
public readonly record struct DateRange(DateTimeOffset Start, DateTimeOffset End);

// ❌ Too large for struct (24+ bytes) - use sealed record
public record struct LargeStruct(string A, string B, string C); // ❌
public sealed record LargeRecord(string A, string B, string C); // ✅
```

#### Allocation testing:
```csharp
// Use BenchmarkDotNet to verify zero allocations
[Benchmark]
public bool TestEquality()
{
    var m1 = new Money(100m, "USD");
    var m2 = new Money(100m, "USD");
    return m1 == m2; // Should be 0 allocations
}
```

## Performance Verification

### Expected results with records:
- **Equality**: 0 allocations, ~1ns
- **Hash code**: 0 allocations, ~1ns  
- **Creation**: Minimal allocations (reference types only)
- **Serialization**: Optimized with System.Text.Json

### Benchmark template:
```csharp
[MemoryDiagnoser]
public class ValueObjectBenchmark
{
    private readonly Money _money1 = new(100m, "USD");
    private readonly Money _money2 = new(100m, "USD");
    
    [Benchmark]
    public bool Equality() => _money1 == _money2;
    
    [Benchmark]
    public int HashCode() => _money1.GetHashCode();
}
```

## Common Optimizations

### 1. String interning for known values
```csharp
public sealed record Currency(string Code)
{
    public string Code { get; } = string.Intern(Code.ToUpperInvariant());
}
```

### 2. Static readonly instances
```csharp
public sealed record Money(decimal Amount, string Currency)
{
    public static readonly Money USDZero = new(0m, "USD");
    public static readonly Money EURZero = new(0m, "EUR");
}
```

### 3. Span-based parsing
```csharp
public sealed record Email(string Value)
{
    public static Email Parse(ReadOnlySpan<char> emailSpan)
    {
        // Span-based parsing for zero allocations
        // when parsing from larger strings
    }
}
```

## Anti-Patterns to Avoid

### ❌ Mutable properties
```csharp
public record Money(decimal Amount, string Currency)
{
    public decimal Amount { get; set; } // ❌ Breaks immutability
}
```

### ❌ Reference equality
```csharp
public sealed record Money(decimal Amount, string Currency)
{
    public override bool Equals(object? obj) => ReferenceEquals(this, obj); // ❌
}
```

### ❌ Large structs
```csharp
// ❌ > 16 bytes, copying overhead
public readonly record struct LargeStruct(Guid Id, DateTime Created, string Name, decimal Value);

// ✅ Use reference type for large objects
public sealed record LargeRecord(Guid Id, DateTime Created, string Name, decimal Value);
```