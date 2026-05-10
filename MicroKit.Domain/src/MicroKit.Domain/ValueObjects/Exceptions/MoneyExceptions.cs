using MicroKit.Domain.Exceptions;

namespace MicroKit.Domain.ValueObjects.Exceptions;

/// <summary>
/// 
/// </summary>
/// <seealso cref="DomainException" />
public class CurrencyMismatchException : DomainException
{
    /// <summary>
    /// Gets the currency1.
    /// </summary>
    /// <value>
    /// The currency1.
    /// </value>
    public string Currency1 { get; } = null!;
    /// <summary>
    /// Gets the currency2.
    /// </summary>
    /// <value>
    /// The currency2.
    /// </value>
    public string Currency2 { get; } = null!;

    /// <summary>
    /// Initializes a new instance of the <see cref="CurrencyMismatchException"/> class.
    /// </summary>
    /// <param name="currency1">The currency1.</param>
    /// <param name="currency2">The currency2.</param>
    public CurrencyMismatchException(string currency1, string currency2)
        : base($"Currency mismatch: {currency1} and {currency2}")
    {
        Currency1 = currency1;
        Currency2 = currency2;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CurrencyMismatchException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public CurrencyMismatchException(string message) : base(message)
    {
    }
}

/// <summary>
/// 
/// </summary>
/// <seealso cref="DomainException" />
public class UnsupportedCurrencyException : DomainException
{
    /// <summary>
    /// Gets the currency.
    /// </summary>
    /// <value>
    /// The currency.
    /// </value>
    public string Currency { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="UnsupportedCurrencyException"/> class.
    /// </summary>
    /// <param name="currency"></param>
    public UnsupportedCurrencyException(string currency)
        : base($"Unsupported currency: {currency}")
    {
        Currency = currency;
    }
}

/// <summary>
/// 
/// </summary>
/// <seealso cref="DomainException" />
public class InvalidMoneyOperationException : DomainException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidMoneyOperationException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public InvalidMoneyOperationException(string message) : base(message) { }
}
