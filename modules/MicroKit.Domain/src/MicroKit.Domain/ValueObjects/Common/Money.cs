using System.Globalization;
using MicroKit.Domain.Exceptions;
using MicroKit.Domain.ValueObjects;

namespace MicroKit.Domain.ValueObjects.Common;

/// <summary>
/// Represents a monetary amount in an ISO 4217 currency.
/// Provides arithmetic operations with currency validation and minor-unit precision.
/// </summary>
/// <param name="Amount">The monetary amount</param>
/// <param name="Currency">The currency, obtained through <see cref="Common.Currency.FromCode(string)"/></param>
/// <remarks>
/// An amount never has more decimals than the currency's minor unit, so every <see cref="Money"/>
/// can be settled: <c>EUR 0.10</c> is valid, <c>EUR 0.105</c> and <c>JPY 1.5</c> are not.
/// <see cref="Add"/> and <see cref="Subtract"/> keep that scale by construction; <see cref="Multiply"/>
/// rounds to it with the mode the caller states, and <see cref="Allocate"/> splits an amount
/// without losing a minor unit.
/// <para>
/// Construction throws <see cref="DomainException"/> for an amount finer than the minor unit, a
/// domain rule, and <see cref="ArgumentException"/> for <c>default(Currency)</c>, a programming
/// error. Operations throw <see cref="DomainException"/> for mismatched currencies and
/// <see cref="ArgumentException"/> for invalid arguments.
/// </para>
/// </remarks>
public sealed record Money(decimal Amount, Currency Currency) : IValueObject
{
    /// <summary>
    /// Gets the currency of this amount.
    /// </summary>
    public Currency Currency { get; } = ValidateCurrency(Currency);

    /// <summary>
    /// Gets the monetary amount, with at most <see cref="Common.Currency.MinorUnit"/> decimals.
    /// </summary>
    public decimal Amount { get; } = ValidateAmount(Amount, Currency);

    /// <summary>
    /// Gets a value indicating whether this monetary amount is zero.
    /// </summary>
    public bool IsZero => Amount == 0;

    /// <summary>
    /// Creates a zero amount for the specified currency.
    /// </summary>
    /// <param name="currency">The currency</param>
    /// <returns>A Money instance representing zero in the specified currency</returns>
    public static Money Zero(Currency currency) => new(0, currency);

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
    /// Multiplies the monetary amount by a scalar factor, rounding the product to the currency's
    /// minor unit.
    /// </summary>
    /// <param name="factor">The multiplication factor</param>
    /// <param name="rounding">
    /// How a product falling between two minor units is rounded. There is no default: an invoice
    /// line, a tax and an interest computation round differently, and the choice is the caller's.
    /// </param>
    /// <returns>A new Money instance representing the rounded product</returns>
    public Money Multiply(decimal factor, MidpointRounding rounding)
    {
        return new Money(Math.Round(Amount * factor, Currency.MinorUnit, rounding), Currency);
    }

    /// <summary>
    /// Splits this amount into shares proportional to <paramref name="ratios"/>, without losing a
    /// minor unit.
    /// </summary>
    /// <param name="ratios">The non-negative weight of each share. At least one must be positive.</param>
    /// <returns>
    /// One amount per ratio, in the same order, summing exactly to this amount. Each share is
    /// rounded toward zero to the minor unit; the minor units left over go one each to the first
    /// shares with a positive ratio. <c>EUR 0.10</c> allocated <c>(1, 1, 1)</c> gives
    /// <c>0.04, 0.03, 0.03</c>.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="ratios"/> is null</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="ratios"/> is empty, contains a negative value, or sums to zero.
    /// </exception>
    /// <exception cref="OverflowException">
    /// Thrown when the amount in minor units multiplied by a ratio exceeds the range of <see cref="decimal"/>.
    /// </exception>
    public IReadOnlyList<Money> Allocate(params int[] ratios)
    {
        ArgumentNullException.ThrowIfNull(ratios);

        if (ratios.Length == 0)
            throw new ArgumentException("At least one ratio is required.", nameof(ratios));

        long total = 0;
        foreach (var ratio in ratios)
        {
            if (ratio < 0)
                throw new ArgumentException("Ratios cannot be negative.", nameof(ratios));
            total += ratio;
        }

        if (total == 0)
            throw new ArgumentException("At least one ratio must be positive.", nameof(ratios));

        // Work in minor units of the absolute amount, then restore the sign, so that a negative
        // amount splits symmetrically to a positive one.
        var scale = Pow10(Currency.MinorUnit);
        var units = Math.Abs(Amount) * scale;
        var shares = new decimal[ratios.Length];
        var remainder = units;

        // Exact integer division: a decimal quotient rounds to 28-29 significant digits and could
        // round up before a floor, over-allocating a share.
        for (var i = 0; i < ratios.Length; i++)
        {
            var product = units * ratios[i];
            shares[i] = (product - product % total) / total;
            remainder -= shares[i];
        }

        // Each positive share loses less than one unit to the floor, so the remainder is smaller
        // than the number of positive shares; a zero-ratio share never receives one.
        for (var i = 0; remainder > 0; i++)
        {
            if (ratios[i] == 0)
                continue;
            shares[i]++;
            remainder--;
        }

        var result = new Money[ratios.Length];
        for (var i = 0; i < shares.Length; i++)
        {
            var share = shares[i] / scale;
            result[i] = new Money(Amount < 0 && share != 0 ? -share : share, Currency);
        }

        return result;
    }

    /// <summary>
    /// Validates that the currency was obtained from the ISO 4217 table.
    /// </summary>
    /// <param name="currency">The currency to validate</param>
    /// <returns>The currency</returns>
    /// <exception cref="ArgumentException">Thrown for <c>default(Currency)</c></exception>
    private static Currency ValidateCurrency(Currency currency)
    {
        if (currency.Code is null)
            throw new ArgumentException(
                "Currency must be obtained through Currency.FromCode.", nameof(currency));

        return currency;
    }

    /// <summary>
    /// Validates that the amount has no more decimals than the currency's minor unit.
    /// </summary>
    /// <param name="amount">The amount to validate</param>
    /// <param name="currency">The currency of the amount</param>
    /// <returns>The amount</returns>
    /// <exception cref="DomainException">Thrown when the amount is finer than the minor unit</exception>
    private static decimal ValidateAmount(decimal amount, Currency currency)
    {
        if (decimal.Round(amount, currency.MinorUnit) != amount)
            throw new DomainException(
                $"Amount {amount.ToString(CultureInfo.InvariantCulture)} has more decimals than {currency.Code} allows ({currency.MinorUnit}).");

        return amount;
    }

    /// <summary>
    /// Validates that two monetary amounts have the same currency.
    /// </summary>
    /// <param name="other">The monetary amount to compare currencies with</param>
    /// <exception cref="DomainException">Thrown when currencies do not match</exception>
    private void ValidateSameCurrency(Money other)
    {
        if (Currency != other.Currency)
        {
            throw new DomainException($"Cannot perform operation on different currencies: {Currency} and {other.Currency}.");
        }
    }

    private static decimal Pow10(int exponent)
    {
        var result = 1m;
        for (var i = 0; i < exponent; i++)
            result *= 10m;
        return result;
    }

    /// <summary>
    /// Returns a string representation of this monetary amount, in the invariant culture with
    /// exactly as many decimals as the currency's minor unit.
    /// </summary>
    /// <returns>A string such as "123.45 EUR", "1500 XOF" or "1.500 TND"</returns>
    public override string ToString() =>
        $"{Amount.ToString("F" + Currency.MinorUnit.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)} {Currency.Code}";

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
}
