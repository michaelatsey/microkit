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
/// The normalized value keeps the ASCII digits, an optional leading <c>+</c>, and an optional
/// extension introduced by <c>x</c> or <c>X</c>, stored as <c>x</c>: <c>+33 (0)6 12-34-56-78 X 12</c>
/// becomes <c>+330612345678x12</c>. Spaces and the formatting characters <c>-</c>, <c>.</c>,
/// <c>(</c>, <c>)</c> and <c>/</c> are removed; any other character, a letter or a non-ASCII digit
/// included, is rejected.
/// </para>
/// <para>
/// The number before the extension has 7 to 15 digits, the ITU-T E.164 maximum; the extension has
/// at least one digit and does not count toward that maximum (ADR-004 D6).
/// </para>
/// </remarks>
public sealed record PhoneNumber(string Value) : IValueObject
{
    private const int MinDigits = 7;
    private const int MaxDigits = 15;
    private const string FormattingCharacters = "-.()/";

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
        var buffer = new char[phoneNumber.Length];
        var length = 0;
        foreach (var c in phoneNumber.Trim())
        {
            if (char.IsAsciiDigit(c) || c == '+')
                buffer[length++] = c;
            else if (c is 'x' or 'X')
                buffer[length++] = 'x';
            else if (!(char.IsWhiteSpace(c) && !char.IsControl(c)) && !FormattingCharacters.Contains(c))
                throw new DomainException(
                    "Phone number can contain only digits, a leading '+', an extension marker 'x', spaces and the characters - . ( ) /.");
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

        // ITU-T E.164: at most 15 digits for the number itself; an extension is not part of it.
        var digitCount = mainPart.Count(char.IsAsciiDigit);
        if (digitCount < MinDigits)
            throw new DomainException($"Phone number must contain at least {MinDigits} digits before any extension.");

        if (digitCount > MaxDigits)
            throw new DomainException($"Phone number cannot contain more than {MaxDigits} digits before any extension.");

        return cleaned;
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
    /// <returns>The phone number string value</returns>
    public static implicit operator string(PhoneNumber phoneNumber) => phoneNumber.Value;

    /// <summary>
    /// Returns the phone number value.
    /// </summary>
    /// <returns>The phone number string</returns>
    public override string ToString() => Value;
}