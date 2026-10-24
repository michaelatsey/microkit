using System.Globalization;
using MicroKit.Domain.Exceptions;
using MicroKit.Domain.ValueObjects;

namespace MicroKit.Domain.ValueObjects.Common;

/// <summary>
/// Represents an email address with lightweight format validation and normalization.
/// Provides case-insensitive equality and normalized storage.
/// </summary>
/// <remarks>
/// <para>
/// Leading and trailing white space is removed. White space or a control character inside the
/// address, a line break included, is rejected, so an address can never carry an injected header
/// line into a message built from it.
/// </para>
/// <para>
/// The local part accepts letters, digits and combining marks, of any script, and the RFC 5322
/// <c>atext</c> symbols
/// <c>! # $ % &amp; ' * + - / = ? ^ _ ` { | } ~</c> and the dot; the domain accepts letters, digits,
/// combining marks, <c>-</c> and the dot. Anything else is rejected, so a value is always a single
/// bare address: no comma or semicolon that a header would read as a list of recipients, no angle
/// bracket, no quoted local part, no invisible format character or variation selector
/// (ADR-004 D5).
/// </para>
/// <para>
/// The validation is lexical. It does not check the length or the hyphens of a domain label, and it
/// does not detect look-alike letters from different scripts, as with any internationalized address.
/// </para>
/// </remarks>
/// <param name="Value">The email address value</param>
public sealed record Email(string Value) : IValueObject
{
    private const string LocalPartSymbols = "!#$%&'*+-/=?^_`{|}~.";
    private const string DomainSymbols = "-.";

    /// <summary>
    /// Gets the normalized email address in lowercase.
    /// </summary>
    public string Value { get; } = ValidateAndNormalize(Value);

    /// <summary>
    /// Validates and normalizes an email address.
    /// </summary>
    /// <param name="email">The email address to validate</param>
    /// <returns>The normalized email address</returns>
    /// <exception cref="DomainException">Thrown when the email format is invalid</exception>
    private static string ValidateAndNormalize(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new DomainException("Email address cannot be null or empty.");

        var trimmed = email.Trim();

        if (trimmed.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            throw new DomainException("Email address cannot contain white space or control characters.");

        if (trimmed.Length < 3) // Minimum: a@b
            throw new DomainException("Email address is too short.");

        var atIndex = trimmed.IndexOf('@');
        if (atIndex <= 0) // Must have @ and at least one character before it
            throw new DomainException("Email address must contain a valid @ symbol.");

        if (atIndex == trimmed.Length - 1) // Must have at least one character after @
            throw new DomainException("Email address must contain a domain after @.");

        if (trimmed.Count(c => c == '@') != 1) // Exactly one @
            throw new DomainException("Email address must contain exactly one @ symbol.");

        var localPart = trimmed.Substring(0, atIndex);
        var domainPart = trimmed.Substring(atIndex + 1);

        if (localPart.Length == 0 || localPart.Length > 64)
            throw new DomainException("Email local part length is invalid.");

        if (domainPart.Length == 0 || domainPart.Length > 253)
            throw new DomainException("Email domain part length is invalid.");

        if (trimmed.Length > 254) // RFC 5321 limit - check after parts validation
            throw new DomainException("Email address is too long.");

        if (!domainPart.Contains('.'))
            throw new DomainException("Email domain must contain at least one dot.");

        // Basic character validation (simplified)
        if (localPart.StartsWith('.') || localPart.EndsWith('.') || localPart.Contains(".."))
            throw new DomainException("Email local part contains invalid dot placement.");

        if (domainPart.StartsWith('.') || domainPart.EndsWith('.') || domainPart.Contains(".."))
            throw new DomainException("Email domain contains invalid dot placement.");

        if (!ContainsOnly(localPart, LocalPartSymbols))
            throw new DomainException(
                "Email local part can contain only letters, digits and the characters ! # $ % & ' * + - / = ? ^ _ ` { | } ~ .");

        if (!ContainsOnly(domainPart, DomainSymbols))
            throw new DomainException("Email domain can contain only letters, digits, '-' and '.'.");

        // Normalize to lowercase for consistent storage and comparison
        return trimmed.ToLowerInvariant();
    }

    /// <summary>
    /// Whether <paramref name="part"/> holds only letters, digits and combining marks of any script,
    /// read code point by code point, and the given ASCII symbols.
    /// </summary>
    /// <remarks>
    /// The combining marks (Mn, Mc, Me) are what Indic and other scripts, and decomposed accents, are
    /// written with. The invisible ones that only select a glyph variant, U+034F and the variation
    /// selectors, are excluded. An unpaired surrogate is rejected.
    /// </remarks>
    private static bool ContainsOnly(string part, string symbols)
    {
        for (var i = 0; i < part.Length; i++)
        {
            var c = part[i];
            if (symbols.Contains(c))
                continue;

            var pair = char.IsHighSurrogate(c) && i + 1 < part.Length && char.IsLowSurrogate(part[i + 1]);
            var codePoint = pair ? char.ConvertToUtf32(c, part[i + 1]) : c;
            var category = CharUnicodeInfo.GetUnicodeCategory(codePoint);

            if (!IsLetterDigitOrMark(category) || IsVariantSelector(codePoint))
                return false;

            if (pair)
                i++;
        }

        return true;
    }

    private static bool IsLetterDigitOrMark(UnicodeCategory category) => category is
        UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter
        or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or UnicodeCategory.DecimalDigitNumber
        or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

    private static bool IsVariantSelector(int codePoint) =>
        codePoint is 0x034F or (>= 0x180B and <= 0x180D) or (>= 0xFE00 and <= 0xFE0F) or (>= 0xE0100 and <= 0xE01EF);

    /// <summary>
    /// Implicitly converts a string to an Email.
    /// </summary>
    /// <param name="email">The email string</param>
    /// <returns>An Email instance</returns>
    public static implicit operator Email(string email) => new(email);

    /// <summary>
    /// Implicitly converts an Email to a string.
    /// </summary>
    /// <param name="email">The Email instance</param>
    /// <returns>The email string value, or <see langword="null"/> for a null instance (ADR-004 D10).</returns>
    [return: NotNullIfNotNull(nameof(email))]
    public static implicit operator string?(Email? email) => email?.Value;

    /// <summary>
    /// Returns the email address value.
    /// </summary>
    /// <returns>The email address string</returns>
    public override string ToString() => Value;
}