using MicroKit.Domain.Exceptions;
using MicroKit.Domain.ValueObjects.Common;
using Shouldly;
using Xunit;

namespace MicroKit.Domain.UnitTests.ValueObjects.Common;

public sealed class MoneyTests
{
    private static readonly Currency Usd = Currency.FromCode("USD");
    private static readonly Currency Eur = Currency.FromCode("EUR");

    private static Money M(decimal amount, string code) => new(amount, Currency.FromCode(code));

    [Theory]
    [InlineData(100.50, "USD")]
    [InlineData(0, "EUR")]
    [InlineData(-50, "GBP")]
    [InlineData(1500, "XOF")]
    [InlineData(1.234, "TND")]
    public void Constructor_ValidParameters_ShouldCreateInstance(decimal amount, string currency)
    {
        // Act
        var money = M(amount, currency);

        // Assert
        money.Amount.ShouldBe(amount);
        money.Currency.Code.ShouldBe(currency);
    }

    [Theory]
    [InlineData(0.105, "EUR")]
    [InlineData(1.5, "JPY")]
    [InlineData(1.5, "XOF")]
    [InlineData(1.2345, "KWD")]
    [InlineData(-0.001, "USD")]
    public void Constructor_ExcessScale_ShouldThrowDomainException(decimal amount, string currency)
    {
        Should.Throw<DomainException>(() => M(amount, currency));
    }

    [Fact]
    public void Constructor_TrailingZerosWithinTheMinorUnit_AreAccepted()
    {
        M(1.500m, "EUR").ShouldBe(M(1.5m, "EUR"));
    }

    [Fact]
    public void Constructor_DefaultCurrency_ShouldThrowArgumentException()
    {
        Should.Throw<ArgumentException>(() => new Money(1, default));
    }

    [Fact]
    public void IsZero_WhenAmountIsZero_ShouldReturnTrue()
    {
        M(0, "USD").IsZero.ShouldBeTrue();
    }

    [Fact]
    public void IsZero_WhenAmountIsNotZero_ShouldReturnFalse()
    {
        M(100, "USD").IsZero.ShouldBeFalse();
    }

    [Fact]
    public void Zero_ShouldReturnZeroAmountWithSpecifiedCurrency()
    {
        // Act
        var money = Money.Zero(Eur);

        // Assert
        money.Amount.ShouldBe(0);
        money.Currency.ShouldBe(Eur);
        money.IsZero.ShouldBeTrue();
    }

    [Fact]
    public void Add_SameCurrency_ShouldReturnSum()
    {
        // Act
        var result = M(100, "USD").Add(M(50, "USD"));

        // Assert
        result.Amount.ShouldBe(150);
        result.Currency.ShouldBe(Usd);
    }

    [Fact]
    public void Add_DifferentCurrency_ShouldThrowDomainException()
    {
        var ex = Should.Throw<DomainException>(() => M(100, "USD").Add(M(50, "EUR")));
        ex.Message.ShouldBe("Cannot perform operation on different currencies: USD and EUR.");
    }

    [Fact]
    public void Add_CaseInsensitiveCurrency_ShouldWork()
    {
        var result = M(100, "USD").Add(M(50, "usd"));

        result.Amount.ShouldBe(150);
        result.Currency.ShouldBe(Usd);
    }

    [Fact]
    public void Subtract_SameCurrency_ShouldReturnDifference()
    {
        var result = M(100, "USD").Subtract(M(30, "USD"));

        result.Amount.ShouldBe(70);
        result.Currency.ShouldBe(Usd);
    }

    [Fact]
    public void Multiply_ShouldReturnProduct()
    {
        var result = M(100, "USD").Multiply(1.5m, MidpointRounding.ToEven);

        result.Amount.ShouldBe(150);
        result.Currency.ShouldBe(Usd);
    }

    [Theory]
    [InlineData(MidpointRounding.ToEven, 0.12)]
    [InlineData(MidpointRounding.AwayFromZero, 0.13)]
    [InlineData(MidpointRounding.ToZero, 0.12)]
    public void Multiply_RoundsToTheMinorUnitWithTheStatedMode(MidpointRounding rounding, decimal expected)
    {
        // 0.25 * 0.5 = 0.125, halfway between two cents
        M(0.25m, "EUR").Multiply(0.5m, rounding).Amount.ShouldBe(expected);
    }

    [Fact]
    public void Multiply_ZeroMinorUnit_RoundsToWholeUnits()
    {
        M(1001, "XOF").Multiply(0.5m, MidpointRounding.AwayFromZero).Amount.ShouldBe(501);
    }

    [Fact]
    public void Allocate_EqualRatios_GivesTheRemainderToTheFirstShares()
    {
        var shares = M(0.10m, "EUR").Allocate(1, 1, 1);

        shares.Select(s => s.Amount).ShouldBe([0.04m, 0.03m, 0.03m]);
    }

    [Fact]
    public void Allocate_SumsExactlyToTheOriginal()
    {
        var original = M(100.00m, "EUR");

        var shares = original.Allocate(3, 7, 11);

        shares.Aggregate((a, b) => a + b).ShouldBe(original);
        shares.ShouldAllBe(s => s.Currency == original.Currency);
    }

    [Fact]
    public void Allocate_UnequalRatios_SplitsProportionally()
    {
        M(5, "XOF").Allocate(3, 7).Select(s => s.Amount).ShouldBe([2m, 3m]);
    }

    [Fact]
    public void Allocate_ZeroRatio_NeverReceivesARemainder()
    {
        M(0.03m, "EUR").Allocate(0, 1, 1).Select(s => s.Amount).ShouldBe([0m, 0.02m, 0.01m]);
    }

    [Fact]
    public void Allocate_NegativeAmount_SplitsSymmetrically()
    {
        M(-0.10m, "EUR").Allocate(1, 1, 1).Select(s => s.Amount).ShouldBe([-0.04m, -0.03m, -0.03m]);
    }

    [Fact]
    public void Allocate_ThreeDecimalCurrency_UsesItsMinorUnit()
    {
        M(1.000m, "TND").Allocate(1, 1, 1).Select(s => s.Amount).ShouldBe([0.334m, 0.333m, 0.333m]);
    }

    [Fact]
    public void Allocate_AtTheEdgeOfDecimalPrecision_StillSumsExactly()
    {
        // 3q + 2 minor units with 29 significant digits: a decimal quotient rounds up before Floor.
        var original = M(79228162514264337593543950334m, "XOF");

        var shares = original.Allocate(1, 1, 1);

        shares.Aggregate((a, b) => a + b).ShouldBe(original);
        shares.Select(s => s.Amount).ShouldBe(
            [26409387504754779197847983445m, 26409387504754779197847983445m, 26409387504754779197847983444m]);
    }

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 1, -1 })]
    [InlineData(new[] { 0, 0 })]
    public void Allocate_InvalidRatios_ThrowsArgumentException(int[] ratios)
    {
        Should.Throw<ArgumentException>(() => M(1, "EUR").Allocate(ratios));
    }

    [Fact]
    public void Allocate_NullRatios_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => M(1, "EUR").Allocate(null!));
    }

    [Fact]
    public void OperatorAdd_ShouldWorkLikeAddMethod()
    {
        var money1 = M(100, "USD");
        var money2 = M(50, "USD");

        (money1 + money2).ShouldBe(money1.Add(money2));
    }

    [Fact]
    public void OperatorSubtract_ShouldWorkLikeSubtractMethod()
    {
        var money1 = M(100, "USD");
        var money2 = M(30, "USD");

        (money1 - money2).ShouldBe(money1.Subtract(money2));
    }

    [Theory]
    [InlineData(123.45, "USD", "123.45 USD")]
    [InlineData(1500, "XOF", "1500 XOF")]
    [InlineData(1500, "JPY", "1500 JPY")]
    [InlineData(1.5, "TND", "1.500 TND")]
    [InlineData(2, "KWD", "2.000 KWD")]
    [InlineData(-0.5, "EUR", "-0.50 EUR")]
    public void ToString_UsesTheInvariantCultureAndTheMinorUnit(decimal amount, string currency, string expected)
    {
        M(amount, currency).ToString().ShouldBe(expected);
    }

    [Fact]
    public void ToString_IgnoresTheCurrentCulture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");

            M(1234.5m, "EUR").ToString().ShouldBe("1234.50 EUR");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(100, "USD", 100, "USD", true)]
    [InlineData(100, "USD", 100, "usd", true)] // Case insensitive
    [InlineData(100, "USD", 50, "USD", false)]
    [InlineData(100, "USD", 100, "EUR", false)]
    public void Equality_ShouldWorkCorrectly(decimal amount1, string currency1, decimal amount2, string currency2, bool expected)
    {
        // Arrange
        var money1 = M(amount1, currency1);
        var money2 = M(amount2, currency2);

        // Act & Assert
        money1.Equals(money2).ShouldBe(expected);
        (money1 == money2).ShouldBe(expected);
        (money1 != money2).ShouldBe(!expected);
    }

    [Fact]
    public void GetHashCode_EqualObjects_ShouldHaveSameHashCode()
    {
        M(100, "USD").GetHashCode().ShouldBe(M(100, "usd").GetHashCode());
    }

    [Fact]
    public void GetHashCode_DifferentObjects_ShouldHaveDifferentHashCodes()
    {
        M(100, "USD").GetHashCode().ShouldNotBe(M(100, "EUR").GetHashCode());
    }

    [Fact]
    public void Add_WithNull_ShouldThrowArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => M(100, "USD").Add(null!));
    }

    [Fact]
    public void Subtract_WithNull_ShouldThrowArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => M(100, "USD").Subtract(null!));
    }
}
