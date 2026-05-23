using BenchmarkDotNet.Attributes;
using MicroKit.Domain.ValueObjects.Common;
using MicroKit.Domain.Exceptions;
using MicroKit.Domain.Identifiers;

namespace MicroKit.Domain.Benchmarks;

/// <summary>
/// Benchmarks comparing modern record-based value objects
/// with traditional abstract base class implementations.
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
public class ValueObjectBenchmarks
{
    private readonly LegacyMoney _legacyMoney1;
    private readonly LegacyMoney _legacyMoney2;
    private readonly LegacyMoney _legacyMoneyDifferent;

    private readonly Money _modernMoney1;
    private readonly Money _modernMoney2;
    private readonly Money _modernMoneyDifferent;

    private readonly LegacyCustomerId _legacyId1;
    private readonly LegacyCustomerId _legacyId2;

    private readonly BenchmarkCustomerId _modernId1;
    private readonly BenchmarkCustomerId _modernId2;

    public ValueObjectBenchmarks()
    {
        // Money benchmarks
        _legacyMoney1 = new LegacyMoney(100.50m, "USD");
        _legacyMoney2 = new LegacyMoney(100.50m, "USD");
        _legacyMoneyDifferent = new LegacyMoney(200.75m, "EUR");

        _modernMoney1 = new Money(100.50m, "USD");
        _modernMoney2 = new Money(100.50m, "USD");
        _modernMoneyDifferent = new Money(200.75m, "EUR");

        // ID benchmarks
        var testGuid = Guid.NewGuid();
        _legacyId1 = new LegacyCustomerId(testGuid);
        _legacyId2 = new LegacyCustomerId(testGuid);

        _modernId1 = new BenchmarkCustomerId(testGuid);
        _modernId2 = new BenchmarkCustomerId(testGuid);
    }

    [Benchmark(Description = "Legacy Money Creation")]
    public LegacyMoney CreateLegacyMoney() => new(99.99m, "USD");

    [Benchmark(Description = "Modern Money Creation")]
    public Money CreateModernMoney() => new(99.99m, "USD");

    [Benchmark(Baseline = true, Description = "Legacy Money Equality (True)")]
    public bool LegacyMoneyEqualityTrue() => _legacyMoney1.Equals(_legacyMoney2);

    [Benchmark(Description = "Modern Money Equality (True)")]
    public bool ModernMoneyEqualityTrue() => _modernMoney1.Equals(_modernMoney2);

    [Benchmark(Description = "Legacy Money Equality (False)")]
    public bool LegacyMoneyEqualityFalse() => _legacyMoney1.Equals(_legacyMoneyDifferent);

    [Benchmark(Description = "Modern Money Equality (False)")]
    public bool ModernMoneyEqualityFalse() => _modernMoney1.Equals(_modernMoneyDifferent);

    [Benchmark(Description = "Legacy Money HashCode")]
    public int LegacyMoneyHashCode() => _legacyMoney1.GetHashCode();

    [Benchmark(Description = "Modern Money HashCode")]
    public int ModernMoneyHashCode() => _modernMoney1.GetHashCode();

    [Benchmark(Description = "Legacy ID Creation")]
    public LegacyCustomerId CreateLegacyId() => new(Guid.NewGuid());

    [Benchmark(Description = "Modern ID Creation")]
    public BenchmarkCustomerId CreateModernId() => BenchmarkCustomerId.New();

    [Benchmark(Description = "Legacy ID Equality")]
    public bool LegacyIdEquality() => _legacyId1.Equals(_legacyId2);

    [Benchmark(Description = "Modern ID Equality")]
    public bool ModernIdEquality() => _modernId1.Equals(_modernId2);

    [Benchmark(Description = "Legacy ID HashCode")]
    public int LegacyIdHashCode() => _legacyId1.GetHashCode();

    [Benchmark(Description = "Modern ID HashCode")]
    public int ModernIdHashCode() => _modernId1.GetHashCode();
}

// Legacy implementations for comparison

/// <summary>
/// Legacy abstract base class for value objects.
/// Uses IEnumerable with LINQ for equality - generates allocations.
/// </summary>
public abstract class LegacyValueObject
{
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public override bool Equals(object? obj)
    {
        if (obj is not LegacyValueObject other || GetType() != other.GetType())
            return false;

        return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    }

    public override int GetHashCode()
    {
        return GetEqualityComponents()
            .Where(x => x is not null)
            .Aggregate(1, (current, obj) => current * 23 + obj!.GetHashCode());
    }
}

/// <summary>
/// Legacy money implementation using abstract base class.
/// Demonstrates the allocation overhead of traditional DDD patterns.
/// </summary>
public sealed class LegacyMoney : LegacyValueObject
{
    public decimal Amount { get; }
    public string Currency { get; }

    public LegacyMoney(decimal amount, string currency)
    {
        if (amount < 0) throw new DomainException("Amount cannot be negative");
        if (string.IsNullOrWhiteSpace(currency)) throw new DomainException("Currency is required");

        Amount = amount;
        Currency = currency.ToUpperInvariant();
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    public override string ToString() => $"{Amount:F2} {Currency}";
}

/// <summary>
/// Legacy customer ID implementation using abstract base class.
/// Shows allocation overhead even for simple identifiers.
/// </summary>
public sealed class LegacyCustomerId : LegacyValueObject
{
    public Guid Value { get; }

    public LegacyCustomerId(Guid value)
    {
        if (value == Guid.Empty) throw new DomainException("Customer ID cannot be empty");
        Value = value;
    }

    public static LegacyCustomerId New() => new(Guid.NewGuid());

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value.ToString();
}

