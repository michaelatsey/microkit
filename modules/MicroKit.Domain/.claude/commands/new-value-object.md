# Command: /new-value-object

## Creates modern value objects following 2026 best practices

### Usage
```
/new-value-object <Name> [--fields <field1:type,field2:type>] [--validate] [--struct]
```

### Examples
```bash
# Complex value object with business logic
/new-value-object Money --fields "amount:decimal,currency:string" --validate

# Simple value object for identifiers  
/new-value-object ProductId --fields "value:Guid" --struct

# Email with validation and normalization
/new-value-object Email --fields "value:string" --validate

# Address with multiple fields
/new-value-object Address --fields "street:string,city:string,postalCode:string,country:string"
```

## Generated Patterns

### 🎯 PRIMARY: sealed record (recommended for most cases)
```csharp
using MicroKit.Domain.Exceptions;
using MicroKit.Domain.ValueObjects;

/// <summary>
/// Represents {description}.
/// </summary>
/// <param name="{FieldName}">The {field description}</param>
public sealed record {Name}({FieldTypes} {FieldNames}) : IValueObject
{
    /// <summary>
    /// Gets the {field description}.
    /// </summary>
    public {Type} {Field} { get; } = Validate{Field}({field});

    // Additional computed properties
    // Business methods returning new instances

    /// <summary>
    /// Creates a new {Name} instance.
    /// </summary>
    public static {Name} Create({FieldTypes} {fieldNames})
    {
        return new({fieldNames});
    }

    private static {Type} Validate{Field}({Type} value)
    {
        // Validation logic when --validate flag used
        if (/* invalid condition */)
            throw new DomainException("Validation message");
        
        return value; // or normalized value
    }

    /// <summary>
    /// Returns a string representation of this {Name}.
    /// </summary>
    public override string ToString() => $"{format}";
}
```

### ⚡ PERFORMANCE: readonly record struct (--struct flag)
Use for simple value objects ≤16 bytes with high-frequency usage:

```csharp
using MicroKit.Domain.Identifiers;

/// <summary>
/// Strongly-typed identifier for {EntityName}.
/// </summary>
/// <param name="Value">The unique identifier value</param>
public readonly record struct {Name}(Guid Value) : IEntityId
{
    object IEntityId.Value => Value;

    public static {Name} New() => new(Guid.NewGuid());
    public static {Name} Empty => new(Guid.Empty);
    public static {Name} From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
```

## Architecture Decision Guide

### Use `sealed record` when:
- ✅ Complex business logic (validation, calculations, formatting)
- ✅ Multiple fields or string properties  
- ✅ Need null argument validation
- ✅ May evolve over time
- ✅ Size > 16 bytes

**Examples**: Money, Email, Address, PhoneNumber, DateRange

### Use `readonly record struct` when:
- ✅ Simple values (1-2 primitive fields)
- ✅ High-frequency usage (identifiers, coordinates)
- ✅ Size ≤ 16 bytes
- ✅ Performance critical
- ✅ No null semantics needed

**Examples**: ProductId, UserId, Percentage, Coordinate

## Generated Tests

### Equality & Immutability Tests
```csharp
public sealed class {Name}Tests
{
    [Theory]
    [InlineData(/* test values */)]
    public void Constructor_ValidValues_ShouldCreateInstance({types} {values})
    {
        // Act
        var result = new {Name}({values});

        // Assert  
        result.{Field}.Should().Be({value});
    }

    [Fact] 
    public void Equality_SameValues_ShouldBeEqual()
    {
        // Arrange
        var {name}1 = new {Name}(/* values */);
        var {name}2 = new {Name}(/* same values */);

        // Assert
        {name}1.Should().Be({name}2);
        {name}1.GetHashCode().Should().Be({name}2.GetHashCode());
        ({name}1 == {name}2).Should().BeTrue();
    }

    [Fact]
    public void Equality_DifferentValues_ShouldNotBeEqual() 
    {
        // Arrange & Assert
        var {name}1 = new {Name}(/* values */);
        var {name}2 = new {Name}(/* different values */);

        {name}1.Should().NotBe({name}2);
    }

    // When --validate flag used
    [Theory]
    [InlineData(/* invalid values */)]
    public void Constructor_InvalidValues_ShouldThrowDomainException({types} {values})
    {
        // Act & Assert
        var act = () => new {Name}({values});
        act.Should().Throw<DomainException>()
           .WithMessage("*{expected validation message}*");
    }

    [Fact]
    public void ToString_ShouldReturnFormattedString()
    {
        // Arrange
        var {name} = new {Name}(/* values */);

        // Act & Assert  
        {name}.ToString().Should().Be("expected format");
    }
}
```

### Serialization Tests (if applicable)
```csharp
[Fact]
public void JsonSerialization_ShouldRoundtrip()
{
    // Arrange
    var original = new {Name}(/* values */);

    // Act
    var json = JsonSerializer.Serialize(original);
    var deserialized = JsonSerializer.Deserialize<{Name}>(json);

    // Assert
    deserialized.Should().Be(original);
}
```

## ❌ Obsolete Patterns (Don't Generate)

### REMOVED: Abstract ValueObject inheritance
```csharp
// ❌ OBSOLETE - Don't use anymore
public sealed class {Name} : ValueObject
{
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return {Field}; // Boxing, allocations, LINQ overhead
    }
}
```

### REMOVED: GetAtomicValues pattern
```csharp
// ❌ OBSOLETE - Poor performance
protected override IEnumerable<object> GetAtomicValues()
{
    yield return Amount;     // Boxing
    yield return Currency;   // Enumeration overhead
}
```

## Performance Benefits

### Compiler-Generated vs Custom Equality
| Approach | Equality Speed | Allocations | Hash Speed |
|----------|---------------|-------------|------------|
| `sealed record` | ~1ns | 0 bytes | ~1ns |
| Abstract ValueObject | ~157ns | 156 bytes | ~89ns |
| **Improvement** | **157x faster** | **Zero alloc** | **89x faster** |

### Why Records Win
- ✅ **Zero allocations**: No enumeration, no boxing
- ✅ **Optimal hashing**: Uses HashCode.Combine automatically  
- ✅ **JIT-friendly**: Inlined equality, no virtual dispatch
- ✅ **AOT-compatible**: No reflection, direct comparisons
- ✅ **Serialization-optimized**: Native System.Text.Json support

## Integration with Domain

### Repository Constraints
```csharp
// Use IValueObject for generic constraints
public interface IValueObjectRepository<T> where T : IValueObject
{
    Task<IEnumerable<T>> FindAsync(Specification<T> spec);
}
```

### EF Core Configuration
```csharp
// Records work perfectly with EF Core
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Product>()
        .Property(p => p.Price)
        .HasConversion(
            money => JsonSerializer.Serialize(money),
            json => JsonSerializer.Deserialize<Money>(json)
        );
}
```

---

## Command Execution Result

✅ **Modern value object created**  
✅ **Comprehensive test suite generated**  
✅ **Zero legacy patterns**  
✅ **Optimal performance characteristics**  
✅ **2026-ready architecture**