using FluentAssertions;
using MicroKit.Domain.Exceptions;
using MicroKit.Domain.ValueObjects.Common;
using Xunit;

namespace MicroKit.Domain.UnitTests.ValueObjects.Common;

public class MoneyTests
{
    [Theory]
    [InlineData(100.50, "USD")]
    [InlineData(0, "EUR")]
    [InlineData(-50, "GBP")]
    public void Constructor_ValidParameters_ShouldCreateInstance(decimal amount, string currency)
    {
        // Act
        var money = new Money(amount, currency);

        // Assert
        money.Amount.Should().Be(amount);
        money.Currency.Should().Be(currency.ToUpperInvariant());
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Constructor_InvalidCurrency_ShouldThrowDomainException(string? currency)
    {
        // Act & Assert
        var act = () => new Money(100, currency!);
        act.Should().Throw<DomainException>()
           .WithMessage("Currency code cannot be null or empty.");
    }

    [Theory]
    [InlineData("US")]
    [InlineData("USDA")]
    [InlineData("12")]
    [InlineData("U1D")]
    public void Constructor_InvalidCurrencyFormat_ShouldThrowDomainException(string currency)
    {
        // Act & Assert
        var act = () => new Money(100, currency);
        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData("usd", "USD")]
    [InlineData("eur", "EUR")]
    [InlineData(" GBP ", "GBP")]
    public void Constructor_CurrencyNormalization_ShouldNormalizeCurrency(string input, string expected)
    {
        // Act
        var money = new Money(100, input);

        // Assert
        money.Currency.Should().Be(expected);
    }

    [Fact]
    public void IsZero_WhenAmountIsZero_ShouldReturnTrue()
    {
        // Arrange
        var money = new Money(0, "USD");

        // Act & Assert
        money.IsZero.Should().BeTrue();
    }

    [Fact]
    public void IsZero_WhenAmountIsNotZero_ShouldReturnFalse()
    {
        // Arrange
        var money = new Money(100, "USD");

        // Act & Assert
        money.IsZero.Should().BeFalse();
    }

    [Fact]
    public void Zero_ShouldReturnZeroAmountWithSpecifiedCurrency()
    {
        // Act
        var money = Money.Zero("EUR");

        // Assert
        money.Amount.Should().Be(0);
        money.Currency.Should().Be("EUR");
        money.IsZero.Should().BeTrue();
    }

    [Fact]
    public void Add_SameCurrency_ShouldReturnSum()
    {
        // Arrange
        var money1 = new Money(100, "USD");
        var money2 = new Money(50, "USD");

        // Act
        var result = money1.Add(money2);

        // Assert
        result.Amount.Should().Be(150);
        result.Currency.Should().Be("USD");
    }

    [Fact]
    public void Add_DifferentCurrency_ShouldThrowDomainException()
    {
        // Arrange
        var money1 = new Money(100, "USD");
        var money2 = new Money(50, "EUR");

        // Act & Assert
        var act = () => money1.Add(money2);
        act.Should().Throw<DomainException>()
           .WithMessage("Cannot perform operation on different currencies: USD and EUR.");
    }

    [Fact]
    public void Add_CaseInsensitiveCurrency_ShouldWork()
    {
        // Arrange
        var money1 = new Money(100, "USD");
        var money2 = new Money(50, "usd");

        // Act
        var result = money1.Add(money2);

        // Assert
        result.Amount.Should().Be(150);
        result.Currency.Should().Be("USD");
    }

    [Fact]
    public void Subtract_SameCurrency_ShouldReturnDifference()
    {
        // Arrange
        var money1 = new Money(100, "USD");
        var money2 = new Money(30, "USD");

        // Act
        var result = money1.Subtract(money2);

        // Assert
        result.Amount.Should().Be(70);
        result.Currency.Should().Be("USD");
    }

    [Fact]
    public void Multiply_ShouldReturnProduct()
    {
        // Arrange
        var money = new Money(100, "USD");

        // Act
        var result = money.Multiply(1.5m);

        // Assert
        result.Amount.Should().Be(150);
        result.Currency.Should().Be("USD");
    }

    [Fact]
    public void OperatorAdd_ShouldWorkLikeAddMethod()
    {
        // Arrange
        var money1 = new Money(100, "USD");
        var money2 = new Money(50, "USD");

        // Act
        var result = money1 + money2;

        // Assert
        result.Should().Be(money1.Add(money2));
    }

    [Fact]
    public void OperatorSubtract_ShouldWorkLikeSubtractMethod()
    {
        // Arrange
        var money1 = new Money(100, "USD");
        var money2 = new Money(30, "USD");

        // Act
        var result = money1 - money2;

        // Assert
        result.Should().Be(money1.Subtract(money2));
    }

    [Fact]
    public void OperatorMultiply_ShouldWorkLikeMultiplyMethod()
    {
        // Arrange
        var money = new Money(100, "USD");

        // Act
        var result1 = money * 1.5m;
        var result2 = 1.5m * money;

        // Assert
        result1.Should().Be(money.Multiply(1.5m));
        result2.Should().Be(money.Multiply(1.5m));
    }

    [Fact]
    public void ToString_ShouldReturnFormattedString()
    {
        // Arrange
        var money = new Money(123.45m, "USD");

        // Act
        var result = money.ToString();

        // Assert
        result.Should().Be("123.45 USD");
    }

    [Theory]
    [InlineData(100, "USD", 100, "USD", true)]
    [InlineData(100, "USD", 100, "usd", true)] // Case insensitive
    [InlineData(100, "USD", 50, "USD", false)]
    [InlineData(100, "USD", 100, "EUR", false)]
    public void Equality_ShouldWorkCorrectly(decimal amount1, string currency1, decimal amount2, string currency2, bool expected)
    {
        // Arrange
        var money1 = new Money(amount1, currency1);
        var money2 = new Money(amount2, currency2);

        // Act & Assert
        money1.Equals(money2).Should().Be(expected);
        (money1 == money2).Should().Be(expected);
        (money1 != money2).Should().Be(!expected);
    }

    [Fact]
    public void GetHashCode_EqualObjects_ShouldHaveSameHashCode()
    {
        // Arrange
        var money1 = new Money(100, "USD");
        var money2 = new Money(100, "usd");

        // Act & Assert
        money1.GetHashCode().Should().Be(money2.GetHashCode());
    }

    [Fact]
    public void GetHashCode_DifferentObjects_ShouldHaveDifferentHashCodes()
    {
        // Arrange
        var money1 = new Money(100, "USD");
        var money2 = new Money(100, "EUR");

        // Act & Assert
        money1.GetHashCode().Should().NotBe(money2.GetHashCode());
    }

    [Fact]
    public void Add_WithNull_ShouldThrowArgumentNullException()
    {
        // Arrange
        var money = new Money(100, "USD");

        // Act & Assert
        var act = () => money.Add(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Subtract_WithNull_ShouldThrowArgumentNullException()
    {
        // Arrange
        var money = new Money(100, "USD");

        // Act & Assert
        var act = () => money.Subtract(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}