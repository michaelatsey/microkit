using MicroKit.Domain.Exceptions;
using MicroKit.Domain.ValueObjects.Common;
using Shouldly;
using Xunit;

namespace MicroKit.Domain.UnitTests.ValueObjects.Common;

public sealed class EmailTests
{
    [Theory]
    [InlineData("test@example.com")]
    [InlineData("user.name@domain.co.uk")]
    [InlineData("firstname+lastname@company.org")]
    [InlineData("test123@test-domain.com")]
    public void Constructor_ValidEmail_ShouldCreateInstance(string email)
    {
        // Act
        var emailObj = new Email(email);

        // Assert
        emailObj.Value.ShouldBe(email.ToLowerInvariant());
    }

    [Theory]
    [InlineData("TEST@EXAMPLE.COM", "test@example.com")]
    [InlineData("User.Name@Domain.COM", "user.name@domain.com")]
    [InlineData("  test@example.com  ", "test@example.com")]
    public void Constructor_EmailNormalization_ShouldNormalizeEmail(string input, string expected)
    {
        // Act
        var email = new Email(input);

        // Assert
        email.Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_InvalidEmail_ShouldThrowDomainException(string? email)
    {
        // Act & Assert
        var ex = Should.Throw<DomainException>(() => new Email(email!));
        ex.Message.ShouldBe("Email address cannot be null or empty.");
    }

    [Theory]
    [InlineData("victim@example.com\r\nBcc: attacker@example.org")]
    [InlineData("victim@example.com\nBcc:attacker@example.org")]
    [InlineData("john doe@example.com")]
    [InlineData("john@exam ple.com")]
    [InlineData("john\t@example.com")]
    [InlineData("john\u0000@example.com")]
    [InlineData("john\u00A0doe@example.com")]
    public void Constructor_WhiteSpaceOrControlCharacterInside_ThrowsDomainException(string email)
    {
        // A line break would let an address inject a header into a message built from it.
        Should.Throw<DomainException>(() => new Email(email))
            .Message.ShouldBe("Email address cannot contain white space or control characters.");
    }

    [Theory]
    [InlineData("victim@example.com,attacker@example.org")]   // a header would read a recipient list
    [InlineData("a@b.c,evil")]
    [InlineData("a;b@example.com")]
    [InlineData("a<b>@example.com")]
    [InlineData("\"john\"@example.com")]
    [InlineData("admin\u200B@example.com")]                    // zero-width space, invisible
    [InlineData("admin@exam\u202Eple.com")]                    // right-to-left override
    [InlineData("a@exa_mple.com")]
    [InlineData("a@[192.168.0.1]")]
    public void Constructor_CharacterOutsideTheAllowedSets_ThrowsDomainException(string email)
    {
        Should.Throw<DomainException>(() => new Email(email));
    }

    [Fact]
    public void Constructor_DecomposedAccent_IsNormalizedToNfcAndEqualsThePrecomposedForm()
    {
        var decomposed = new Email("jose\u0301@exemple.fr");

        decomposed.Value.ShouldBe("jos\u00E9@exemple.fr");
        decomposed.ShouldBe(new Email("jos\u00E9@exemple.fr"));
    }

    [Fact]
    public void Constructor_LetterOutsideTheBmp_Succeeds()
    {
        // U+20000, a CJK Extension B ideograph, encoded as a surrogate pair. Built in code because
        // xUnit's theory-data serialization is not reliable for surrogates.
        var email = char.ConvertFromUtf32(0x20000) + "@example.com";

        new Email(email).Value.ShouldBe(email);
    }

    [Theory]
    [InlineData("admin\uFE0F@example.com")]   // variation selector, invisible
    [InlineData("ad\u034Fmin@example.com")]   // combining grapheme joiner, invisible
    [InlineData("a\u180Fb@example.com")]      // Mongolian free variation selector four
    [InlineData("a\u3164b@example.com")]      // Hangul filler, an invisible letter (Lo)
    [InlineData("a\u17B4b@example.com")]      // Khmer inherent vowel, invisible (Mn)
    [InlineData("a\u00ADb@example.com")]      // soft hyphen
    [InlineData("\u0645\u06CC\u200C\u062E\u0648\u0627\u0645@example.com")]   // ZWNJ, a documented limit
    public void Constructor_InvisibleVariantSelector_ThrowsDomainException(string email)
    {
        Should.Throw<DomainException>(() => new Email(email));
    }

    [Fact]
    public void Constructor_UnpairedSurrogate_ThrowsDomainException()
    {
        Should.Throw<DomainException>(() => new Email("ab" + '\uD800' + "@example.com"));
    }

    [Theory]
    [InlineData("o'brien@example.com")]
    [InlineData("a!#$%&*/=?^_`{|}~-z@example.com")]
    [InlineData("jos\u00E9@exemple.fr")]
    [InlineData("user@b\u00FCcher.de")]
    [InlineData("\u0928\u092E\u0938\u094D\u0924\u0947@example.com")]              // Devanagari, Mc and Mn signs
    [InlineData("user@\u092E\u0930\u093E\u0920\u0940.\u092D\u093E\u0930\u0924")]  // Devanagari domain
    public void Constructor_AllowedCharacters_Succeed(string email)
    {
        new Email(email).Value.ShouldBe(email.ToLowerInvariant());
    }

    [Fact]
    public void ImplicitConversionToString_NullEmail_ReturnsNull()
    {
        // Declared non-null on purpose: the conversion must cope with a null that slipped through.
        Email email = null!;

        string? value = email;

        value.ShouldBeNull();
    }

    [Theory]
    [InlineData("a")]
    [InlineData("@")]
    [InlineData("ab")]
    public void Constructor_TooShortEmail_ShouldThrowDomainException(string email)
    {
        // Act & Assert
        var ex = Should.Throw<DomainException>(() => new Email(email));
        ex.Message.ShouldBe("Email address is too short.");
    }

    [Fact]
    public void Constructor_TooLongEmail_ShouldThrowDomainException()
    {
        // Arrange - Create an email longer than 254 characters (but local part under 64 limit)
        var localPart = new string('a', 50); // 50 characters - under 64 limit
        var domainPart = new string('b', 200) + ".com"; // 204 characters - under 253 limit
        var longEmail = $"{localPart}@{domainPart}"; // 50 + 1 + 204 = 255 characters > 254

        // Act & Assert
        var ex = Should.Throw<DomainException>(() => new Email(longEmail));
        ex.Message.ShouldBe("Email address is too long.");
    }

    [Theory]
    [InlineData("testexample.com")]        // Missing @
    [InlineData("test@@example.com")]      // Multiple @
    [InlineData("test@")]                  // Missing domain
    [InlineData("@example.com")]           // Missing local part
    public void Constructor_InvalidFormat_ShouldThrowDomainException(string email)
    {
        // Act & Assert
        Should.Throw<DomainException>(() => new Email(email));
    }

    [Theory]
    [InlineData("test@example")]           // Domain missing dot
    [InlineData("test@")]                  // Empty domain
    public void Constructor_InvalidDomain_ShouldThrowDomainException(string email)
    {
        // Act & Assert
        Should.Throw<DomainException>(() => new Email(email));
    }

    [Theory]
    [InlineData(".test@example.com")]      // Local part starts with dot
    [InlineData("test.@example.com")]      // Local part ends with dot
    [InlineData("te..st@example.com")]     // Consecutive dots in local part
    public void Constructor_InvalidLocalPartDots_ShouldThrowDomainException(string email)
    {
        // Act & Assert
        Should.Throw<DomainException>(() => new Email(email));
    }

    [Theory]
    [InlineData("test@.example.com")]      // Domain starts with dot
    [InlineData("test@example.com.")]      // Domain ends with dot
    [InlineData("test@exam..ple.com")]     // Consecutive dots in domain
    public void Constructor_InvalidDomainDots_ShouldThrowDomainException(string email)
    {
        // Act & Assert
        Should.Throw<DomainException>(() => new Email(email));
    }

    [Fact]
    public void Constructor_LocalPartTooLong_ShouldThrowDomainException()
    {
        // Arrange - Create local part longer than 64 characters
        var longLocalPart = new string('a', 65);
        var email = $"{longLocalPart}@example.com";

        // Act & Assert
        var ex = Should.Throw<DomainException>(() => new Email(email));
        ex.Message.ShouldBe("Email local part length is invalid.");
    }

    [Fact]
    public void Constructor_DomainPartTooLong_ShouldThrowDomainException()
    {
        // Arrange - Create domain part longer than 253 characters
        var longDomain = new string('a', 252) + ".com"; // 252 + 4 = 256 characters
        var email = $"test@{longDomain}";

        // Act & Assert
        var ex = Should.Throw<DomainException>(() => new Email(email));
        ex.Message.ShouldBe("Email domain part length is invalid.");
    }

    [Fact]
    public void ImplicitConversionFromString_ShouldWork()
    {
        // Arrange
        const string emailString = "test@example.com";

        // Act
        Email email = emailString;

        // Assert
        email.Value.ShouldBe(emailString);
    }

    [Fact]
    public void ImplicitConversionToString_ShouldWork()
    {
        // Arrange
        var email = new Email("test@example.com");

        // Act
        string emailString = email;

        // Assert
        emailString.ShouldBe("test@example.com");
    }

    [Fact]
    public void ToString_ShouldReturnEmailValue()
    {
        // Arrange
        var email = new Email("test@example.com");

        // Act
        var result = email.ToString();

        // Assert
        result.ShouldBe("test@example.com");
    }

    [Theory]
    [InlineData("test@example.com", "test@example.com", true)]
    [InlineData("test@example.com", "TEST@EXAMPLE.COM", true)]    // Case insensitive
    [InlineData("test@example.com", "test@other.com", false)]
    public void Equality_ShouldWorkCorrectly(string email1, string email2, bool expected)
    {
        // Arrange
        var emailObj1 = new Email(email1);
        var emailObj2 = new Email(email2);

        // Act & Assert
        emailObj1.Equals(emailObj2).ShouldBe(expected);
        (emailObj1 == emailObj2).ShouldBe(expected);
        (emailObj1 != emailObj2).ShouldBe(!expected);
    }

    [Fact]
    public void GetHashCode_CaseInsensitiveEmails_ShouldHaveSameHashCode()
    {
        // Arrange
        var email1 = new Email("test@example.com");
        var email2 = new Email("TEST@EXAMPLE.COM");

        // Act & Assert
        email1.GetHashCode().ShouldBe(email2.GetHashCode());
    }

    [Fact]
    public void GetHashCode_DifferentEmails_ShouldHaveDifferentHashCodes()
    {
        // Arrange
        var email1 = new Email("test@example.com");
        var email2 = new Email("test@other.com");

        // Act & Assert
        email1.GetHashCode().ShouldNotBe(email2.GetHashCode());
    }

    [Theory]
    [InlineData("test@example.com")]
    [InlineData("user.name+tag@domain.co.uk")]
    [InlineData("firstname-lastname@company.org")]
    public void Constructor_CommonValidFormats_ShouldSucceed(string email)
    {
        // Act & Assert
        Should.NotThrow(() => new Email(email));
    }
}
