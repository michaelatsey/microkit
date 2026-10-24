using System.Globalization;
using MicroKit.Domain.Exceptions;
using MicroKit.Domain.ValueObjects;

namespace MicroKit.Domain.ValueObjects.Common;

/// <summary>
/// Represents a phone number with normalization and international compatibility.
/// Provides basic validation while preserving international format flexibility.
/// </summary>
/// <param name="Value">The phone number value</param>
/// <remarks>
/// <para>
/// The normalized value keeps the digits, an optional leading <c>+</c>, and an optional extension,
/// stored as <c>x</c>: <c>+33 6 12-34-56-78 ext. 12</c> becomes <c>+33612345678x12</c>. The
/// extension marker is <c>x</c>, or <c>ext</c>, <c>extn</c> or <c>extension</c> in any case,
/// optionally followed by one of <c>. : = #</c>. The RFC 3966 forms are read too: a leading
/// <c>tel:</c> is dropped and <c>;ext=</c> is a marker. A decimal digit of any script is stored as
/// its ASCII digit. White space and the formatting characters <c>-</c>, <c>.</c>, <c>(</c>,
/// <c>)</c> and <c>/</c> are removed. Any other character is rejected: another letter, a <c>#</c>
/// or <c>*</c> outside a marker, a typographic dash.
/// </para>
/// <para>
/// The digits are kept as written: no national trunk prefix is removed. <c>+33 (0)6 12 34 56 78</c>
/// becomes <c>+330612345678</c>, which is not dialable and is not equal to
/// <c>+33612345678</c>. Remove a trunk prefix written after a country code before building the value.
/// </para>
/// <para>
/// The number before the extension has 7 to 15 digits, the ITU-T E.164 maximum; the extension has
/// 1 to 10 digits and does not count toward that maximum (ADR-004 D6).
/// </para>
/// </remarks>
public sealed record PhoneNumber(string Value) : IValueObject
{
    private const int MinDigits = 7;
    private const int MaxDigits = 15;
    private const int MaxExtensionDigits = 10;
    private const string FormattingCharacters = "-.()/";

    private const string MarkerSeparators = ".:=#";
    private const string TelScheme = "tel:";

    // Longest first, so "extension" is not read as "ext" followed by "ension".
    private static readonly string[] ExtensionWords = ["extension", "extn", "ext", "x"];

    /// <summary>
    /// Gets the normalized phone number value.
    /// </summary>
    public string Value { get; } = ValidateAndNormalize(Value);

    /// <summary>
    /// Validates and normalizes a phone number.
    /// </summary>
    /// <param name="phoneNumber">The phone number to validate</param>
    /// <returns>The normalized phone number</returns>
    /// <exception cref="DomainException">Thrown when the phone number format is invalid</exception>
    private static string ValidateAndNormalize(string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            throw new DomainException("Phone number cannot be null or empty.");

        // Keep the digits, the '+' and the extension marker; drop the formatting; reject the rest.
        // Only digits, '+' and 'x' reach the value, so dropped white space cannot carry a line break.
        var text = phoneNumber.Trim();
        if (text.StartsWith(TelScheme, StringComparison.OrdinalIgnoreCase))
            text = text[TelScheme.Length..];

        var buffer = new char[text.Length];
        var length = 0;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '+')
            {
                buffer[length++] = '+';
                i++;
            }
            else if (ExtensionMarkerLengthAt(text, i) is > 0 and var markerLength)
            {
                buffer[length++] = 'x';
                i += markerLength;
            }
            else if (UnicodeText.CodePointAt(text, i, out var width) is { } codePoint
                && CharUnicodeInfo.GetUnicodeCategory(codePoint) == UnicodeCategory.DecimalDigitNumber)
            {
                buffer[length++] = (char)('0' + CharUnicodeInfo.GetDecimalDigitValue(text, i));
                i += width;
            }
            else if (char.IsWhiteSpace(c) || FormattingCharacters.Contains(c))
                i++;
            else
                throw new DomainException(
                    "Phone number can contain only digits, a leading '+', an extension marker ('x', 'ext', 'extn' or 'extension'), white space and the characters - . ( ) /.");
        }

        var cleaned = new string(buffer, 0, length);

        if (!cleaned.Any(char.IsAsciiDigit))
            throw new DomainException("Phone number must contain at least one digit.");

        if (cleaned.LastIndexOf('+') > 0)
            throw new DomainException("Phone number can contain only one '+' symbol at the beginning.");

        var extensionIndex = cleaned.IndexOf('x');
        if (extensionIndex >= 0 && cleaned.IndexOf('x', extensionIndex + 1) >= 0)
            throw new DomainException("Phone number can contain only one extension marker 'x'.");

        var mainPart = extensionIndex < 0 ? cleaned : cleaned[..extensionIndex];
        if (extensionIndex >= 0 && extensionIndex == cleaned.Length - 1)
            throw new DomainException("Phone number extension must contain at least one digit.");

        if (extensionIndex >= 0 && cleaned.Length - extensionIndex - 1 > MaxExtensionDigits)
            throw new DomainException($"Phone number extension cannot contain more than {MaxExtensionDigits} digits.");

        // ITU-T E.164: at most 15 digits for the number itself; an extension is not part of it.
        var digitCount = mainPart.Count(char.IsAsciiDigit);
        if (digitCount < MinDigits)
            throw new DomainException($"Phone number must contain at least {MinDigits} digits before any extension.");

        if (digitCount > MaxDigits)
            throw new DomainException($"Phone number cannot contain more than {MaxDigits} digits before any extension.");

        return cleaned;
    }

    /// <summary>
    /// Returns the number of characters of an extension marker starting at <paramref name="index"/>,
    /// or 0: an optional <c>;</c> (RFC 3966 <c>;ext=</c>), one of <see cref="ExtensionWords"/> in any
    /// case, then an optional separator <c>. : = #</c>.
    /// </summary>
    private static int ExtensionMarkerLengthAt(string text, int index)
    {
        var position = text[index] == ';' ? index + 1 : index;

        foreach (var word in ExtensionWords)
        {
            if (!text.AsSpan(position).StartsWith(word, StringComparison.OrdinalIgnoreCase))
                continue;

            position += word.Length;
            if (position < text.Length && MarkerSeparators.Contains(text[position]))
                position++;

            return position - index;
        }

        return 0;
    }

    /// <summary>
    /// Implicitly converts a string to a PhoneNumber.
    /// </summary>
    /// <param name="phoneNumber">The phone number string</param>
    /// <returns>A PhoneNumber instance</returns>
    public static implicit operator PhoneNumber(string phoneNumber) => new(phoneNumber);

    /// <summary>
    /// Implicitly converts a PhoneNumber to a string.
    /// </summary>
    /// <param name="phoneNumber">The PhoneNumber instance</param>
    /// <returns>The phone number string value, or <see langword="null"/> for a null instance (ADR-004 D10).</returns>
    [return: NotNullIfNotNull(nameof(phoneNumber))]
    public static implicit operator string?(PhoneNumber? phoneNumber) => phoneNumber?.Value;

    /// <summary>
    /// Returns the phone number value.
    /// </summary>
    /// <returns>The phone number string</returns>
    public override string ToString() => Value;
}