using MicroKit.Domain.ValueObjects;
using MicroKit.Domain.ValueObjects.Exceptions;
using Xunit;

namespace MicroKit.Domain.Tests;

public sealed class MoneyTests
{
    // --- Construction ---

    [Fact]
    public void Constructor_ValidPositiveAmount_Succeeds()
    {
        var m = new Money(100m, "USD");
        Assert.Equal(100m, m.Amount);
        Assert.Equal("USD", m.Currency);
    }

    [Fact]
    public void Constructor_NegativeAmount_Succeeds()
    {
        // Negative money is valid (credits, adjustments, debits)
        var m = new Money(-50m, "USD");
        Assert.Equal(-50m, m.Amount);
    }

    [Fact]
    public void Constructor_Zero_Succeeds()
    {
        var m = new Money(0m, "EUR");
        Assert.Equal(0m, m.Amount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_EmptyCurrency_Throws(string currency)
    {
        Assert.Throws<ArgumentException>(() => new Money(100m, currency));
    }

    [Theory]
    [InlineData("US")]
    [InlineData("USDA")]
    [InlineData("123")]
    [InlineData("U1D")]
    public void Constructor_InvalidCurrencyFormat_Throws(string currency)
    {
        Assert.Throws<ArgumentException>(() => new Money(100m, currency));
    }

    [Fact]
    public void Constructor_LowercaseCurrency_NormalisesToUppercase()
    {
        var m = new Money(10m, "usd");
        Assert.Equal("USD", m.Currency);
    }

    // --- Factories ---

    [Fact]
    public void Zero_ReturnsZeroAmount()
    {
        var m = Money.Zero("EUR");
        Assert.True(m.IsZero());
        Assert.Equal("EUR", m.Currency);
    }

    [Fact]
    public void Create_ReturnsMoney()
    {
        var m = Money.Create(42m, "GBP");
        Assert.Equal(42m, m.Amount);
        Assert.Equal("GBP", m.Currency);
    }

    // --- Arithmetic operators ---

    [Fact]
    public void Add_SameCurrency_ReturnsSum()
    {
        var a = new Money(10m, "USD");
        var b = new Money(5m, "USD");
        Assert.Equal(15m, (a + b).Amount);
    }

    [Fact]
    public void Add_DifferentCurrency_Throws()
    {
        var a = new Money(10m, "USD");
        var b = new Money(5m, "EUR");
        Assert.Throws<CurrencyMismatchException>(() => _ = a + b);
    }

    [Fact]
    public void Subtract_SameCurrency_ReturnsDifference()
    {
        var a = new Money(10m, "USD");
        var b = new Money(3m, "USD");
        Assert.Equal(7m, (a - b).Amount);
    }

    [Fact]
    public void Subtract_ProducesNegative_IsAllowed()
    {
        var a = new Money(3m, "USD");
        var b = new Money(10m, "USD");
        Assert.Equal(-7m, (a - b).Amount);
    }

    [Fact]
    public void Multiply_ReturnsScaledAmount()
    {
        var m = new Money(10m, "USD");
        Assert.Equal(25m, (m * 2.5m).Amount);
    }

    [Fact]
    public void Divide_ReturnsDividedAmount()
    {
        var m = new Money(10m, "USD");
        Assert.Equal(4m, (m / 2.5m).Amount);
    }

    [Fact]
    public void Divide_ByZero_Throws()
    {
        var m = new Money(10m, "USD");
        Assert.Throws<DivideByZeroException>(() => _ = m / 0m);
    }

    // --- Comparison operators ---

    [Fact]
    public void GreaterThan_ReturnsCorrectResult()
    {
        var a = new Money(10m, "USD");
        var b = new Money(5m, "USD");
        Assert.True(a > b);
        Assert.False(b > a);
    }

    [Fact]
    public void LessThan_ReturnsCorrectResult()
    {
        var a = new Money(5m, "USD");
        var b = new Money(10m, "USD");
        Assert.True(a < b);
        Assert.False(b < a);
    }

    [Fact]
    public void GreaterThanOrEqual_Equal_ReturnsTrue()
    {
        var a = new Money(10m, "USD");
        var b = new Money(10m, "USD");
        Assert.True(a >= b);
        Assert.True(b >= a);
    }

    [Fact]
    public void LessThanOrEqual_Equal_ReturnsTrue()
    {
        var a = new Money(10m, "USD");
        var b = new Money(10m, "USD");
        Assert.True(a <= b);
        Assert.True(b <= a);
    }

    [Fact]
    public void Comparison_DifferentCurrency_Throws()
    {
        var a = new Money(10m, "USD");
        var b = new Money(5m, "EUR");
        Assert.Throws<CurrencyMismatchException>(() => _ = a > b);
    }

    // --- State queries ---

    [Fact]
    public void IsZero_ZeroAmount_ReturnsTrue()
        => Assert.True(Money.Zero("USD").IsZero());

    [Fact]
    public void IsPositive_PositiveAmount_ReturnsTrue()
        => Assert.True(new Money(1m, "USD").IsPositive());

    [Fact]
    public void IsNegative_NegativeAmount_ReturnsTrue()
        => Assert.True(new Money(-1m, "USD").IsNegative());

    // --- Negate / Abs ---

    [Fact]
    public void Negate_PositiveAmount_ReturnsNegative()
    {
        var m = new Money(50m, "USD");
        Assert.Equal(-50m, m.Negate().Amount);
    }

    [Fact]
    public void Negate_NegativeAmount_ReturnsPositive()
    {
        var m = new Money(-50m, "USD");
        Assert.Equal(50m, m.Negate().Amount);
    }

    [Fact]
    public void Abs_NegativeAmount_ReturnsPositive()
    {
        var m = new Money(-30m, "USD");
        Assert.Equal(30m, m.Abs().Amount);
    }

    // --- Sum ---

    [Fact]
    public void Sum_ValidCollection_ReturnsTotalAmount()
    {
        var amounts = new[] { new Money(10m, "USD"), new Money(20m, "USD"), new Money(5m, "USD") };
        var result = Money.Sum(amounts);
        Assert.Equal(35m, result.Amount);
        Assert.Equal("USD", result.Currency);
    }

    [Fact]
    public void Sum_EmptyCollection_Throws()
    {
        Assert.Throws<ArgumentException>(() => Money.Sum(Array.Empty<Money>()));
    }

    [Fact]
    public void Sum_MixedCurrencies_Throws()
    {
        var amounts = new[] { new Money(10m, "USD"), new Money(5m, "EUR") };
        Assert.Throws<CurrencyMismatchException>(() => Money.Sum(amounts));
    }

    [Fact]
    public void Sum_SingleItem_ReturnsThatAmount()
    {
        var amounts = new[] { new Money(42m, "CHF") };
        Assert.Equal(42m, Money.Sum(amounts).Amount);
    }

    // --- Average ---

    [Fact]
    public void Average_ValidCollection_ReturnsAverage()
    {
        var amounts = new[] { new Money(10m, "USD"), new Money(20m, "USD") };
        var avg = Money.Average(amounts);
        Assert.Equal(15m, avg.Amount);
    }

    [Fact]
    public void Average_EmptyCollection_Throws()
    {
        Assert.Throws<ArgumentException>(() => Money.Average(Array.Empty<Money>()));
    }

    // --- Equality (via ValueObject) ---

    [Fact]
    public void Equality_SameAmountAndCurrency_AreEqual()
    {
        var a = new Money(100m, "USD");
        var b = new Money(100m, "USD");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Equality_DifferentAmount_NotEqual()
    {
        var a = new Money(100m, "USD");
        var b = new Money(200m, "USD");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Equality_DifferentCurrency_NotEqual()
    {
        var a = new Money(100m, "USD");
        var b = new Money(100m, "EUR");
        Assert.NotEqual(a, b);
    }

    // --- ConvertTo ---

    [Fact]
    public void ConvertTo_ValidRate_ReturnsConvertedAmount()
    {
        var m = new Money(100m, "USD");
        var converted = m.ConvertTo("EUR", 0.92m);
        Assert.Equal(92m, converted.Amount);
        Assert.Equal("EUR", converted.Currency);
    }

    [Fact]
    public void ConvertTo_ZeroRate_Throws()
    {
        var m = new Money(100m, "USD");
        Assert.Throws<ArgumentException>(() => m.ConvertTo("EUR", 0m));
    }

    [Fact]
    public void ConvertTo_EmptyTargetCurrency_Throws()
    {
        var m = new Money(100m, "USD");
        Assert.Throws<ArgumentException>(() => m.ConvertTo("", 1.0m));
    }

    // --- Discount / Tax ---

    [Fact]
    public void CalculateDiscount_TenPercent_ReturnsTenPercent()
    {
        var m = new Money(200m, "USD");
        var discount = m.CalculateDiscount(10m);
        Assert.Equal(20m, discount.Amount);
    }

    [Fact]
    public void CalculateDiscount_InvalidPercentage_Throws()
    {
        var m = new Money(100m, "USD");
        Assert.Throws<ArgumentException>(() => m.CalculateDiscount(101m));
        Assert.Throws<ArgumentException>(() => m.CalculateDiscount(-1m));
    }

    [Fact]
    public void ApplyDiscount_TenPercent_ReducesAmount()
    {
        var m = new Money(200m, "USD");
        var discounted = m.ApplyDiscount(10m);
        Assert.Equal(180m, discounted.Amount);
    }

    [Fact]
    public void CalculateTax_TwentyPercent_ReturnsTaxAmount()
    {
        var m = new Money(100m, "USD");
        var tax = m.CalculateTax(0.20m);
        Assert.Equal(20m, tax.Amount);
    }

    [Fact]
    public void CalculateTax_NegativeRate_Throws()
    {
        var m = new Money(100m, "USD");
        Assert.Throws<ArgumentException>(() => m.CalculateTax(-0.1m));
    }

    [Fact]
    public void AddTax_TwentyPercent_IncreasesAmount()
    {
        var m = new Money(100m, "USD");
        var withTax = m.AddTax(0.20m);
        Assert.Equal(120m, withTax.Amount);
    }

    // --- SmallestUnit round-trip ---

    [Fact]
    public void ToSmallestUnit_USD_ReturnsCents()
    {
        var m = new Money(10.50m, "USD");
        Assert.Equal(1050L, m.ToSmallestUnit());
    }

    [Fact]
    public void FromSmallestUnit_USD_ReturnsCorrectAmount()
    {
        var m = Money.FromSmallestUnit(1050L, "USD");
        Assert.Equal(10.50m, m.Amount);
    }
}
