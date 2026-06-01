using MicroKit.Domain.Exceptions;
using MicroKit.Domain.ValueObjects.Common;
using Shouldly;
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
        money.Amount.ShouldBe(amount);
        money.Currency.ShouldBe(currency.ToUpperInvariant());
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Constructor_InvalidCurrency_ShouldThrowDomainException(string? currency)
    {
        // Act & Assert
        var ex = Should.Throw<DomainException>(() => new Money(100, currency!));
        ex.Message.ShouldBe("Currency code cannot be null or empty.");
    }

    [Theory]
    [InlineData("US")]
    [InlineData("USDA")]
    [InlineData("12")]
    [InlineData("U1D")]
    public void Constructor_InvalidCurrencyFormat_ShouldThrowDomainException(string currency)
    {
        // Act & Assert
        Should.Throw<DomainException>(() => new Money(100, currency));
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
        money.Currency.ShouldBe(expected);
    }

    [Fact]
    public void IsZero_WhenAmountIsZero_ShouldReturnTrue()
    {
        // Arrange
        var money = new Money(0, "USD");

        // Act & Assert
        money.IsZero.ShouldBeTrue();
    }

    [Fact]
    public void IsZero_WhenAmountIsNotZero_ShouldReturnFalse()
    {
        // Arrange
        var money = new Money(100, "USD");

        // Act & Assert
        money.IsZero.ShouldBeFalse();
    }

    [Fact]
    public void Zero_ShouldReturnZeroAmountWithSpecifiedCurrency()
    {
        // Act
        var money = Money.Zero("EUR");

        // Assert
        money.Amount.ShouldBe(0);
        money.Currency.ShouldBe("EUR");
        money.IsZero.ShouldBeTrue();
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
        result.Amount.ShouldBe(150);
        result.Currency.ShouldBe("USD");
    }

    [Fact]
    public void Add_DifferentCurrency_ShouldThrowDomainException()
    {
        // Arrange
        var money1 = new Money(100, "USD");
        var money2 = new Money(50, "EUR");

        // Act & Assert
        var ex = Should.Throw<DomainException>(() => money1.Add(money2));
        ex.Message.ShouldBe("Cannot perform operation on different currencies: USD and EUR.");
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
        result.Amount.ShouldBe(150);
        result.Currency.ShouldBe("USD");
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
        result.Amount.ShouldBe(70);
        result.Currency.ShouldBe("USD");
    }

    [Fact]
    public void Multiply_ShouldReturnProduct()
    {
        // Arrange
        var money = new Money(100, "USD");

        // Act
        var result = money.Multiply(1.5m);

        // Assert
        result.Amount.ShouldBe(150);
        result.Currency.ShouldBe("USD");
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
        result.ShouldBe(money1.Add(money2));
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
        result.ShouldBe(money1.Subtract(money2));
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
        result1.ShouldBe(money.Multiply(1.5m));
        result2.ShouldBe(money.Multiply(1.5m));
    }

    [Fact]
    public void ToString_ShouldReturnFormattedString()
    {
        // Arrange
        var money = new Money(123.45m, "USD");

        // Act
        var result = money.ToString();

        // Assert
        result.ShouldBe("123.45 USD");
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
        money1.Equals(money2).ShouldBe(expected);
        (money1 == money2).ShouldBe(expected);
        (money1 != money2).ShouldBe(!expected);
    }

    [Fact]
    public void GetHashCode_EqualObjects_ShouldHaveSameHashCode()
    {
        // Arrange
        var money1 = new Money(100, "USD");
        var money2 = new Money(100, "usd");

        // Act & Assert
        money1.GetHashCode().ShouldBe(money2.GetHashCode());
    }

    [Fact]
    public void GetHashCode_DifferentObjects_ShouldHaveDifferentHashCodes()
    {
        // Arrange
        var money1 = new Money(100, "USD");
        var money2 = new Money(100, "EUR");

        // Act & Assert
        money1.GetHashCode().ShouldNotBe(money2.GetHashCode());
    }

    [Fact]
    public void Add_WithNull_ShouldThrowArgumentNullException()
    {
        // Arrange
        var money = new Money(100, "USD");

        // Act & Assert
        Should.Throw<ArgumentNullException>(() => money.Add(null!));
    }

    [Fact]
    public void Subtract_WithNull_ShouldThrowArgumentNullException()
    {
        // Arrange
        var money = new Money(100, "USD");

        // Act & Assert
        Should.Throw<ArgumentNullException>(() => money.Subtract(null!));
    }
}
