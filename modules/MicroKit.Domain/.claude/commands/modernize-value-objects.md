# Command: /modernize-value-objects

## Analyzes and modernizes value objects to 2026 best practices

### Usage
```
/modernize-value-objects [path]
```

### Description
Reviews all value objects in the specified path (or current directory) and applies 2026 modernization:

1. **Remove unused abstract base classes**
2. **Optimize record type selection** (sealed record vs readonly record struct)
3. **Update validation patterns**
4. **Add performance optimizations**
5. **Ensure immutability compliance**

### What gets analyzed:
- ✅ **Type selection**: sealed record vs readonly record struct
- ✅ **Size optimization**: Struct size limits (≤16 bytes)
- ✅ **Allocation patterns**: Zero-allocation equality/hashing
- ✅ **Immutability**: No mutable properties
- ✅ **Validation**: Constructor validation patterns
- ✅ **Serialization**: System.Text.Json compatibility
- ✅ **Performance**: Benchmark-ready implementations

### Optimization decisions:

#### → `readonly record struct` when:
- Simple value (1-2 primitive fields)
- Size ≤ 16 bytes
- High-frequency usage
- Examples: IDs, percentages, coordinates

#### → `sealed record` when:
- Complex value objects
- Multiple string properties
- Business methods
- Size > 16 bytes
- Examples: Money, Address, Email

### Example transformations:

#### Before (outdated pattern):
```csharp
public class Percentage : ValueObject
{
    private readonly decimal _value;
    
    public Percentage(decimal value)
    {
        if (value < 0 || value > 100)
            throw new ArgumentException("Invalid percentage");
        _value = value;
    }
    
    public decimal Value => _value;
    
    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return _value;
    }
}
```

#### After (2026 modern):
```csharp
public readonly record struct Percentage(decimal Value)
{
    public Percentage
    {
        if (Value < 0 || Value > 100)
            throw new DomainException("Percentage must be between 0 and 100");
    }
    
    public decimal AsFraction => Value / 100m;
    public static Percentage FromFraction(decimal fraction) => new(fraction * 100m);
}
```

### Performance improvements:
- **Equality**: ~100x faster (0 allocations vs multiple allocations)
- **Hash codes**: ~50x faster (optimized vs LINQ aggregation)
- **Memory**: ~90% reduction (no base class overhead)
- **JIT**: Better inlining and optimizations

### Compatibility checks:
- ✅ **EF Core**: Records work perfectly
- ✅ **System.Text.Json**: Optimized serialization
- ✅ **NativeAOT**: No reflection dependencies
- ✅ **Trimming**: Trim-safe implementations

### Output report:
```
Value Objects Modernization Report
==================================

Analyzed: 8 value objects
Optimized: 3 → readonly record struct  
Modernized: 5 → sealed record
Removed: 1 unused base class

Performance gains:
- Equality: 0 allocations (was 156 bytes/op)
- Hash: 0 allocations (was 96 bytes/op)
- Memory: -78% object overhead

Breaking changes: None
Compatibility: 100% maintained
```

### Integration with build:
- Runs during CI/CD
- Fails build if anti-patterns detected
- Suggests optimizations via analyzers
- Benchmarks verify performance gains

### Example usage:
```bash
# Analyze current directory
/modernize-value-objects

# Analyze specific path  
/modernize-value-objects src/Domain/ValueObjects

# Include performance benchmarks
/modernize-value-objects --benchmark

# Apply fixes automatically
/modernize-value-objects --fix
```