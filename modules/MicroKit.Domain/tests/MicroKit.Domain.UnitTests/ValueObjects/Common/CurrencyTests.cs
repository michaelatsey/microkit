using MicroKit.Domain.Exceptions;
using MicroKit.Domain.ValueObjects.Common;
using Shouldly;
using Xunit;

namespace MicroKit.Domain.UnitTests.ValueObjects.Common;

public sealed class CurrencyTests
{
    [Theory]
    [InlineData("XOF", 0)]
    [InlineData("JPY", 0)]
    [InlineData("EUR", 2)]
    [InlineData("USD", 2)]
    [InlineData("TND", 3)]
    [InlineData("KWD", 3)]
    [InlineData("CLF", 4)]
    public void FromCode_KnownCode_CarriesItsMinorUnit(string code, int minorUnit)
    {
        var currency = Currency.FromCode(code);

        currency.Code.ShouldBe(code);
        currency.MinorUnit.ShouldBe(minorUnit);
    }

    [Theory]
    [InlineData("eur")]
    [InlineData(" EUR ")]
    public void FromCode_NormalizesCaseAndWhiteSpace(string code)
    {
        Currency.FromCode(code).ShouldBe(Currency.FromCode("EUR"));
    }

    [Theory]
    [InlineData("ABC")]
    [InlineData("XYZ")]
    [InlineData("US")]
    [InlineData("USDA")]
    [InlineData("U1D")]
    [InlineData("HRK")] // withdrawn
    [InlineData("XAU")] // minor unit N.A.
    [InlineData("XXX")] // minor unit N.A.
    public void FromCode_UnknownCode_ThrowsDomainException(string code)
    {
        Should.Throw<DomainException>(() => Currency.FromCode(code));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void FromCode_Empty_ThrowsDomainException(string? code)
    {
        var ex = Should.Throw<DomainException>(() => Currency.FromCode(code!));
        ex.Message.ShouldBe("Currency code cannot be null or empty.");
    }

    [Fact]
    public void ToString_ReturnsTheCode()
    {
        Currency.FromCode("TND").ToString().ShouldBe("TND");
        default(Currency).ToString().ShouldBe(string.Empty);
    }
}
