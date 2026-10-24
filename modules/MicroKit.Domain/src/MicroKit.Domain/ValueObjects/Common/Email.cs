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
/// bracket, no quoted local part, no invisible character such as a format character, a variation
/// selector or a Hangul filler (ADR-004 D5).
/// </para>
/// <para>
/// The address is normalized to Unicode NFC before it is checked and stored, so a decomposed accent
/// and its precomposed form give equal values.
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

        if (!UnicodeText.IsVisible(trimmed))
            throw new DomainException("Email address cannot contain an invisible character.");

        // NFC: a decomposed accent and its precomposed form are the same address, so they must compare
        // equal and collide in a unique index. Normalize throws on an unpaired surrogate, refused above.
        trimmed = trimmed.Normalize();

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
        return trimmed.ToLowerInvariant().Normalize();
    }

    /// <summary>
    /// Whether <paramref name="part"/> holds only letters, digits and combining marks of any script,
    /// read code point by code point, and the given ASCII symbols.
    /// </summary>
    /// <remarks>
    /// The combining marks (Mn, Mc, Me) are what Indic and other scripts are written with. A code point
    /// <see cref="UnicodeText"/> refuses, invisible or unassigned, is rejected even when it is a letter
    /// or a mark, and so is an unpaired surrogate.
    /// </remarks>
    private static bool ContainsOnly(string part, string symbols)
    {
        var i = 0;
        while (i < part.Length)
        {
            if (symbols.Contains(part[i]))
            {
                i++;
                continue;
            }

            if (UnicodeText.CodePointAt(part, i, out var length) is not { } codePoint
                || UnicodeText.IsRefused(codePoint)
                || !IsLetterDigitOrMark(CharUnicodeInfo.GetUnicodeCategory(codePoint)))
                return false;

            i += length;
        }

        return true;
    }

    private static bool IsLetterDigitOrMark(UnicodeCategory category) => category is
        UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter
        or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or UnicodeCategory.DecimalDigitNumber
        or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

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