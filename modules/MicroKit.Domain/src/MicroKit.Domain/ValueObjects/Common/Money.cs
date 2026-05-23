using MicroKit.Domain.Exceptions;
using MicroKit.Domain.ValueObjects;

namespace MicroKit.Domain.ValueObjects.Common;

/// <summary>
/// Represents a monetary amount with a specific currency.
/// Provides arithmetic operations with currency validation and ISO 4217 compliance.
/// </summary>
/// <param name="Amount">The monetary amount</param>
/// <param name="Currency">The ISO 4217 currency code</param>
public sealed record Money(decimal Amount, string Currency) : IValueObject
{
    /// <summary>
    /// Gets the monetary amount.
    /// </summary>
    public decimal Amount { get; } = Amount;

    /// <summary>
    /// Gets the ISO 4217 currency code in uppercase.
    /// </summary>
    public string Currency { get; } = ValidateCurrency(Currency);

    /// <summary>
    /// Gets a value indicating whether this monetary amount is zero.
    /// </summary>
    public bool IsZero => Amount == 0;

    /// <summary>
    /// Creates a zero amount for the specified currency.
    /// </summary>
    /// <param name="currency">The ISO 4217 currency code</param>
    /// <returns>A Money instance representing zero in the specified currency</returns>
    public static Money Zero(string currency) => new(0, currency);

    /// <summary>
    /// Adds two monetary amounts of the same currency.
    /// </summary>
    /// <param name="other">The monetary amount to add</param>
    /// <returns>A new Money instance representing the sum</returns>
    /// <exception cref="DomainException">Thrown when currencies do not match</exception>
    public Money Add(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        ValidateSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    /// <summary>
    /// Subtracts a monetary amount from this amount.
    /// </summary>
    /// <param name="other">The monetary amount to subtract</param>
    /// <returns>A new Money instance representing the difference</returns>
    /// <exception cref="DomainException">Thrown when currencies do not match</exception>
    public Money Subtract(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        ValidateSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    /// <summary>
    /// Multiplies the monetary amount by a scalar factor.
    /// </summary>
    /// <param name="factor">The multiplication factor</param>
    /// <returns>A new Money instance representing the product</returns>
    public Money Multiply(decimal factor)
    {
        return new Money(Amount * factor, Currency);
    }

    /// <summary>
    /// Validates that the currency matches the ISO 4217 format and normalizes it.
    /// </summary>
    /// <param name="currency">The currency code to validate</param>
    /// <returns>The normalized currency code</returns>
    /// <exception cref="DomainException">Thrown when the currency format is invalid</exception>
    private static string ValidateCurrency(string currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
            throw new DomainException("Currency code cannot be null or empty.");

        var normalized = currency.Trim().ToUpperInvariant();

        if (normalized.Length != 3)
            throw new DomainException("Currency code must be exactly 3 characters long.");

        if (!normalized.All(char.IsLetter))
            throw new DomainException("Currency code must contain only letters.");

        return normalized;
    }

    /// <summary>
    /// Validates that two monetary amounts have the same currency.
    /// </summary>
    /// <param name="other">The monetary amount to compare currencies with</param>
    /// <exception cref="DomainException">Thrown when currencies do not match</exception>
    private void ValidateSameCurrency(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException($"Cannot perform operation on different currencies: {Currency} and {other.Currency}.");
        }
    }

    /// <summary>
    /// Returns a string representation of this monetary amount.
    /// </summary>
    /// <returns>A string in the format "123.45 USD"</returns>
    public override string ToString() => $"{Amount:F2} {Currency}";

    /// <summary>
    /// Addition operator for Money instances.
    /// </summary>
    /// <param name="left">The first monetary amount</param>
    /// <param name="right">The second monetary amount</param>
    /// <returns>A new Money instance representing the sum</returns>
    public static Money operator +(Money left, Money right) => left.Add(right);

    /// <summary>
    /// Subtraction operator for Money instances.
    /// </summary>
    /// <param name="left">The first monetary amount</param>
    /// <param name="right">The second monetary amount</param>
    /// <returns>A new Money instance representing the difference</returns>
    public static Money operator -(Money left, Money right) => left.Subtract(right);

    /// <summary>
    /// Multiplication operator for Money and decimal.
    /// </summary>
    /// <param name="money">The monetary amount</param>
    /// <param name="factor">The multiplication factor</param>
    /// <returns>A new Money instance representing the product</returns>
    public static Money operator *(Money money, decimal factor) => money.Multiply(factor);

    /// <summary>
    /// Multiplication operator for decimal and Money.
    /// </summary>
    /// <param name="factor">The multiplication factor</param>
    /// <param name="money">The monetary amount</param>
    /// <returns>A new Money instance representing the product</returns>
    public static Money operator *(decimal factor, Money money) => money.Multiply(factor);
}