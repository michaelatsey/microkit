using MicroKit.Domain.Exceptions;
using MicroKit.Domain.ValueObjects.Common;
using Shouldly;
using Xunit;

namespace MicroKit.Domain.UnitTests.ValueObjects.Common;

public sealed class PhoneNumberTests
{
    [Theory]
    [InlineData("0612345678", "0612345678")]
    [InlineData("+33 6 12 34 56 78", "+33612345678")]
    [InlineData("+33 (0)6 12-34-56-78", "+330612345678")]   // documented limit: the trunk 0 is kept
    [InlineData("06.12.34.56.78", "0612345678")]
    [InlineData("555/123 4567", "5551234567")]
    [InlineData("  +1 555 123 4567  ", "+15551234567")]
    [InlineData("+1 555 123 4567", "+15551234567")]
    public void Constructor_FormattedNumber_KeepsTheDigitsAndTheLeadingPlus(string input, string expected)
    {
        new PhoneNumber(input).Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("5551234567x12", "5551234567x12")]
    [InlineData("5551234567X12", "5551234567x12")]
    [InlineData("555 123 4567 x 12", "5551234567x12")]
    public void Constructor_Extension_IsKeptWithALowerCaseMarker(string input, string expected)
    {
        new PhoneNumber(input).Value.ShouldBe(expected);
    }

    [Fact]
    public void Equality_ExtensionMarkerCase_DoesNotMatter()
    {
        new PhoneNumber("5551234567X12").ShouldBe(new PhoneNumber("5551234567x12"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_NullOrWhiteSpace_ThrowsDomainException(string? input)
    {
        Should.Throw<DomainException>(() => new PhoneNumber(input!));
    }

    [Theory]
    [InlineData("call me at 0612345678 tomorrow")]
    [InlineData("0612345678#")]
    [InlineData("0612345678 ex 12")]
    [InlineData("tel:0612345678")]
    public void Constructor_CharacterOtherThanDigitsPlusMarkerOrFormatting_ThrowsDomainException(string input)
    {
        Should.Throw<DomainException>(() => new PhoneNumber(input))
            .Message.ShouldStartWith("Phone number can contain only digits");
    }

    [Theory]
    [InlineData("555-123-4567 ext. 89", "5551234567x89")]
    [InlineData("555-123-4567 EXT 89", "5551234567x89")]
    [InlineData("555-123-4567 extension 89", "5551234567x89")]
    [InlineData("555-123-4567 Extension 89", "5551234567x89")]
    public void Constructor_SpelledOutExtensionMarker_IsStoredAsX(string input, string expected)
    {
        new PhoneNumber(input).Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("06\t12345678", "0612345678")]
    [InlineData("06\n12345678", "0612345678")]
    [InlineData("06\r\n12 34 56 78", "0612345678")]
    public void Constructor_AnyWhiteSpace_IsFormatting(string input, string expected)
    {
        // Only digits, '+' and 'x' reach the value, so a dropped line break cannot reach it.
        new PhoneNumber(input).Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("\u0660\u0666\u0661\u0662\u0663\u0664\u0665\u0666\u0667\u0668", "0612345678")] // Arabic-Indic
    [InlineData("\uFF10\uFF16\uFF11\uFF12\uFF13\uFF14\uFF15\uFF16\uFF17\uFF18", "0612345678")] // full-width
    public void Constructor_DecimalDigitOfAnotherScript_IsStoredAsAsciiDigit(string input, string expected)
    {
        new PhoneNumber(input).Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("0612+345678")]
    [InlineData("+33+612345678")]
    [InlineData("0612345678+")]
    public void Constructor_PlusAnywhereButTheStart_ThrowsDomainException(string input)
    {
        Should.Throw<DomainException>(() => new PhoneNumber(input))
            .Message.ShouldBe("Phone number can contain only one '+' symbol at the beginning.");
    }

    [Theory]
    [InlineData("1234567x8x9")]
    [InlineData("5551234567x12X3")]
    public void Constructor_SeveralExtensionMarkers_ThrowsDomainException(string input)
    {
        Should.Throw<DomainException>(() => new PhoneNumber(input))
            .Message.ShouldBe("Phone number can contain only one extension marker 'x'.");
    }

    [Fact]
    public void Constructor_EmptyExtension_ThrowsDomainException()
    {
        Should.Throw<DomainException>(() => new PhoneNumber("5551234567x"))
            .Message.ShouldBe("Phone number extension must contain at least one digit.");
    }

    [Theory]
    [InlineData("123456")]
    [InlineData("123456x7890")]
    [InlineData("x1234567")]
    public void Constructor_FewerThanSevenDigitsBeforeTheExtension_ThrowsDomainException(string input)
    {
        Should.Throw<DomainException>(() => new PhoneNumber(input))
            .Message.ShouldBe("Phone number must contain at least 7 digits before any extension.");
    }

    [Fact]
    public void Constructor_MoreThanFifteenDigits_ThrowsDomainException()
    {
        Should.Throw<DomainException>(() => new PhoneNumber("+1234567890123456"))
            .Message.ShouldBe("Phone number cannot contain more than 15 digits before any extension.");
    }

    [Theory]
    [InlineData("1234567", "1234567")]
    [InlineData("+123456789012345", "+123456789012345")]
    public void Constructor_DigitCountBounds_AreInclusive(string input, string expected)
    {
        new PhoneNumber(input).Value.ShouldBe(expected);
    }

    [Fact]
    public void Constructor_ExtensionOfMoreThanTenDigits_ThrowsDomainException()
    {
        new PhoneNumber("5551234567x1234567890").Value.ShouldBe("5551234567x1234567890");

        Should.Throw<DomainException>(() => new PhoneNumber("5551234567x12345678901"))
            .Message.ShouldBe("Phone number extension cannot contain more than 10 digits.");
    }

    [Fact]
    public void Constructor_ExtensionDigits_DoNotCountTowardTheE164Maximum()
    {
        // 11 digits for the number, 5 for the extension: 16 in all, valid.
        new PhoneNumber("+33612345678 x12345").Value.ShouldBe("+33612345678x12345");
    }

    [Theory]
    [InlineData("()")]
    [InlineData("+")]
    [InlineData("- . /")]
    public void Constructor_NoDigit_ThrowsDomainException(string input)
    {
        Should.Throw<DomainException>(() => new PhoneNumber(input))
            .Message.ShouldBe("Phone number must contain at least one digit.");
    }

    [Fact]
    public void ImplicitConversionToString_NullPhoneNumber_ReturnsNull()
    {
        // Declared non-null on purpose: the conversion must cope with a null that slipped through.
        PhoneNumber phone = null!;

        string? value = phone;

        value.ShouldBeNull();
    }

    [Fact]
    public void ImplicitConversions_FormattedString_RoundTripTheNormalizedValue()
    {
        PhoneNumber phone = "+33 6 12 34 56 78";
        string value = phone;

        value.ShouldBe("+33612345678");
        phone.ToString().ShouldBe("+33612345678");
    }
}
