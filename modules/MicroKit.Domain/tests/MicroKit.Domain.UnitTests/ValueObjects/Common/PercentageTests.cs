using FluentAssertions;
using MicroKit.Domain.Exceptions;
using MicroKit.Domain.ValueObjects.Common;
using Xunit;

namespace MicroKit.Domain.UnitTests.ValueObjects.Common;

public class PercentageTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(25.5)]
    public void Constructor_ValidValue_ShouldCreateInstance(decimal value)
    {
        // Act
        var percentage = new Percentage(value);

        // Assert
        percentage.Value.Should().Be(value);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(-50)]
    [InlineData(100.1)]
    [InlineData(150)]
    public void Constructor_InvalidValue_ShouldThrowDomainException(decimal value)
    {
        // Act & Assert
        var act = () => new Percentage(value);
        act.Should().Throw<DomainException>()
           .WithMessage("Percentage value must be between 0 and 100 inclusive.");
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 0.5)]
    [InlineData(100, 1)]
    [InlineData(25, 0.25)]
    public void AsFraction_ShouldReturnCorrectFraction(decimal value, decimal expectedFraction)
    {
        // Arrange
        var percentage = new Percentage(value);

        // Act & Assert
        percentage.AsFraction.Should().Be(expectedFraction);
    }

    [Theory]
    [InlineData(100, 50, 50)]
    [InlineData(200, 25, 50)]
    [InlineData(0, 50, 0)]
    public void Of_ShouldCalculateCorrectAmount(decimal amount, decimal percentageValue, decimal expected)
    {
        // Arrange
        var percentage = new Percentage(percentageValue);

        // Act
        var result = percentage.Of(amount);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void Add_ValidSum_ShouldReturnCorrectPercentage()
    {
        // Arrange
        var percentage1 = new Percentage(30);
        var percentage2 = new Percentage(20);

        // Act
        var result = percentage1.Add(percentage2);

        // Assert
        result.Value.Should().Be(50);
    }

    [Fact]
    public void Add_SumExceeds100_ShouldThrowDomainException()
    {
        // Arrange
        var percentage1 = new Percentage(60);
        var percentage2 = new Percentage(50);

        // Act & Assert
        var act = () => percentage1.Add(percentage2);
        act.Should().Throw<DomainException>()
           .WithMessage("Percentage value must be between 0 and 100 inclusive.");
    }

    [Fact]
    public void Subtract_ValidDifference_ShouldReturnCorrectPercentage()
    {
        // Arrange
        var percentage1 = new Percentage(70);
        var percentage2 = new Percentage(20);

        // Act
        var result = percentage1.Subtract(percentage2);

        // Assert
        result.Value.Should().Be(50);
    }

    [Fact]
    public void Subtract_DifferenceBelowZero_ShouldThrowDomainException()
    {
        // Arrange
        var percentage1 = new Percentage(30);
        var percentage2 = new Percentage(50);

        // Act & Assert
        var act = () => percentage1.Subtract(percentage2);
        act.Should().Throw<DomainException>()
           .WithMessage("Percentage value must be between 0 and 100 inclusive.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.5)]
    [InlineData(1)]
    [InlineData(0.25)]
    public void FromFraction_ValidFraction_ShouldCreateCorrectPercentage(decimal fraction)
    {
        // Act
        var percentage = Percentage.FromFraction(fraction);

        // Assert
        percentage.AsFraction.Should().Be(fraction);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(2)]
    public void FromFraction_InvalidFraction_ShouldThrowDomainException(decimal fraction)
    {
        // Act & Assert
        var act = () => Percentage.FromFraction(fraction);
        act.Should().Throw<DomainException>()
           .WithMessage("Fraction value must be between 0.0 and 1.0 inclusive.");
    }

    [Fact]
    public void Zero_ShouldReturnZeroPercentage()
    {
        // Act
        var percentage = Percentage.Zero;

        // Assert
        percentage.Value.Should().Be(0);
    }

    [Fact]
    public void OneHundred_ShouldReturnOneHundredPercentage()
    {
        // Act
        var percentage = Percentage.OneHundred;

        // Assert
        percentage.Value.Should().Be(100);
    }

    [Fact]
    public void OperatorAdd_ShouldWorkLikeAddMethod()
    {
        // Arrange
        var percentage1 = new Percentage(30);
        var percentage2 = new Percentage(20);

        // Act
        var result = percentage1 + percentage2;

        // Assert
        result.Should().Be(percentage1.Add(percentage2));
    }

    [Fact]
    public void OperatorSubtract_ShouldWorkLikeSubtractMethod()
    {
        // Arrange
        var percentage1 = new Percentage(70);
        var percentage2 = new Percentage(20);

        // Act
        var result = percentage1 - percentage2;

        // Assert
        result.Should().Be(percentage1.Subtract(percentage2));
    }

    [Fact]
    public void ImplicitConversionFromDecimal_ShouldWork()
    {
        // Act
        Percentage percentage = 50m;

        // Assert
        percentage.Value.Should().Be(50);
    }

    [Fact]
    public void ImplicitConversionToDecimal_ShouldWork()
    {
        // Arrange
        var percentage = new Percentage(50);

        // Act
        decimal value = percentage;

        // Assert
        value.Should().Be(50);
    }

    [Fact]
    public void ToString_ShouldReturnFormattedString()
    {
        // Arrange
        var percentage = new Percentage(75.25m);

        // Act
        var result = percentage.ToString();

        // Assert
        result.Should().Be("75.25%");
    }

    [Theory]
    [InlineData(50, 50, true)]
    [InlineData(50, 60, false)]
    [InlineData(0, 0, true)]
    [InlineData(100, 100, true)]
    public void Equality_ShouldWorkCorrectly(decimal value1, decimal value2, bool expected)
    {
        // Arrange
        var percentage1 = new Percentage(value1);
        var percentage2 = new Percentage(value2);

        // Act & Assert
        percentage1.Equals(percentage2).Should().Be(expected);
        (percentage1 == percentage2).Should().Be(expected);
        (percentage1 != percentage2).Should().Be(!expected);
    }

    [Fact]
    public void GetHashCode_EqualObjects_ShouldHaveSameHashCode()
    {
        // Arrange
        var percentage1 = new Percentage(50);
        var percentage2 = new Percentage(50);

        // Act & Assert
        percentage1.GetHashCode().Should().Be(percentage2.GetHashCode());
    }

    [Fact]
    public void Add_WithNull_ShouldThrowArgumentNullException()
    {
        // Arrange
        var percentage = new Percentage(50);

        // Act & Assert
        var act = () => percentage.Add(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Subtract_WithNull_ShouldThrowArgumentNullException()
    {
        // Arrange
        var percentage = new Percentage(50);

        // Act & Assert
        var act = () => percentage.Subtract(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}