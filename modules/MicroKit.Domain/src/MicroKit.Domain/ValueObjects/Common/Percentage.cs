using MicroKit.Domain.Exceptions;

namespace MicroKit.Domain.ValueObjects.Common;

/// <summary>
/// Represents a percentage value between 0 and 100 inclusive.
/// Provides arithmetic operations and calculations for percentage-based operations.
/// </summary>
/// <param name="Value">The percentage value between 0 and 100</param>
public sealed record Percentage(decimal Value)
{
    /// <summary>
    /// Gets the percentage value between 0 and 100.
    /// </summary>
    public decimal Value { get; } = ValidateRange(Value);

    /// <summary>
    /// Gets the percentage as a decimal fraction (0.0 to 1.0).
    /// </summary>
    public decimal AsFraction => Value / 100m;

    /// <summary>
    /// Validates that the percentage value is within the valid range.
    /// </summary>
    /// <param name="value">The value to validate</param>
    /// <returns>The validated percentage value</returns>
    /// <exception cref="DomainException">Thrown when the value is outside the range 0-100</exception>
    private static decimal ValidateRange(decimal value)
    {
        if (value < 0 || value > 100)
            throw new DomainException("Percentage value must be between 0 and 100 inclusive.");

        return value;
    }

    /// <summary>
    /// Calculates a percentage of the specified amount.
    /// </summary>
    /// <param name="amount">The amount to calculate the percentage of</param>
    /// <returns>The calculated percentage amount</returns>
    public decimal Of(decimal amount)
    {
        return amount * AsFraction;
    }

    /// <summary>
    /// Adds another percentage to this percentage.
    /// </summary>
    /// <param name="other">The percentage to add</param>
    /// <returns>A new Percentage representing the sum</returns>
    /// <exception cref="DomainException">Thrown when the result exceeds 100%</exception>
    public Percentage Add(Percentage other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new Percentage(Value + other.Value);
    }

    /// <summary>
    /// Subtracts another percentage from this percentage.
    /// </summary>
    /// <param name="other">The percentage to subtract</param>
    /// <returns>A new Percentage representing the difference</returns>
    /// <exception cref="DomainException">Thrown when the result is less than 0%</exception>
    public Percentage Subtract(Percentage other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new Percentage(Value - other.Value);
    }

    /// <summary>
    /// Creates a percentage from a decimal value.
    /// </summary>
    /// <param name="value">The percentage value between 0 and 100</param>
    /// <returns>A new Percentage instance</returns>
    /// <exception cref="DomainException">Thrown when the value is outside the range 0-100</exception>
    public static Percentage Create(decimal value)
    {
        return new Percentage(value);
    }

    /// <summary>
    /// Creates a percentage from a fraction (0.0 to 1.0).
    /// </summary>
    /// <param name="fraction">The fraction value between 0.0 and 1.0</param>
    /// <returns>A new Percentage instance</returns>
    /// <exception cref="DomainException">Thrown when the fraction is outside the range 0.0-1.0</exception>
    public static Percentage FromFraction(decimal fraction)
    {
        if (fraction < 0 || fraction > 1)
            throw new DomainException("Fraction value must be between 0.0 and 1.0 inclusive.");

        return new Percentage(fraction * 100m);
    }

    /// <summary>
    /// Represents 0%.
    /// </summary>
    public static Percentage Zero => new(0m);

    /// <summary>
    /// Represents 100%.
    /// </summary>
    public static Percentage OneHundred => new(100m);

    /// <summary>
    /// Implicitly converts a decimal to a Percentage.
    /// </summary>
    /// <param name="value">The percentage value</param>
    /// <returns>A Percentage instance</returns>
    public static implicit operator Percentage(decimal value) => new(value);

    /// <summary>
    /// Implicitly converts a Percentage to a decimal.
    /// </summary>
    /// <param name="percentage">The Percentage instance</param>
    /// <returns>The percentage value</returns>
    public static implicit operator decimal(Percentage percentage) => percentage.Value;

    /// <summary>
    /// Addition operator for Percentage instances.
    /// </summary>
    /// <param name="left">The first percentage</param>
    /// <param name="right">The second percentage</param>
    /// <returns>A new Percentage representing the sum</returns>
    public static Percentage operator +(Percentage left, Percentage right) => left.Add(right);

    /// <summary>
    /// Subtraction operator for Percentage instances.
    /// </summary>
    /// <param name="left">The first percentage</param>
    /// <param name="right">The second percentage</param>
    /// <returns>A new Percentage representing the difference</returns>
    public static Percentage operator -(Percentage left, Percentage right) => left.Subtract(right);

    /// <summary>
    /// Returns a string representation of this percentage.
    /// </summary>
    /// <returns>A formatted string with the percentage symbol</returns>
    public override string ToString() => $"{Value:F2}%";
}