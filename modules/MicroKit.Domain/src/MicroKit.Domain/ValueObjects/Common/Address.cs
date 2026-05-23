using MicroKit.Domain.Exceptions;

namespace MicroKit.Domain.ValueObjects.Common;

/// <summary>
/// Represents a physical address with street, city, postal code, and country information.
/// Provides a generic and reusable address structure suitable for international use.
/// </summary>
/// <param name="Street">The street address including number and street name</param>
/// <param name="City">The city or locality name</param>
/// <param name="PostalCode">The postal or ZIP code</param>
/// <param name="Country">The country name or code</param>
public sealed record Address(string Street, string City, string PostalCode, string Country)
{
    /// <summary>
    /// Gets the street address including number and street name.
    /// </summary>
    public string Street { get; } = ValidateNotEmpty(Street, nameof(Street));

    /// <summary>
    /// Gets the city or locality name.
    /// </summary>
    public string City { get; } = ValidateNotEmpty(City, nameof(City));

    /// <summary>
    /// Gets the postal or ZIP code.
    /// </summary>
    public string PostalCode { get; } = ValidateNotEmpty(PostalCode, nameof(PostalCode));

    /// <summary>
    /// Gets the country name or code.
    /// </summary>
    public string Country { get; } = ValidateNotEmpty(Country, nameof(Country));

    /// <summary>
    /// Creates a new Address instance with the specified components.
    /// </summary>
    /// <param name="street">The street address including number and street name</param>
    /// <param name="city">The city or locality name</param>
    /// <param name="postalCode">The postal or ZIP code</param>
    /// <param name="country">The country name or code</param>
    /// <returns>A new Address instance</returns>
    /// <exception cref="DomainException">Thrown when any component is null or empty</exception>
    public static Address Create(string street, string city, string postalCode, string country)
    {
        return new Address(street, city, postalCode, country);
    }

    /// <summary>
    /// Validates that a string property is not null or empty.
    /// </summary>
    /// <param name="value">The value to validate</param>
    /// <param name="propertyName">The name of the property for error messages</param>
    /// <returns>The trimmed value</returns>
    /// <exception cref="DomainException">Thrown when the value is null or empty</exception>
    private static string ValidateNotEmpty(string value, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException($"Address {propertyName} cannot be null or empty.");

        return value.Trim();
    }

    /// <summary>
    /// Returns a formatted string representation of this address.
    /// </summary>
    /// <returns>A formatted address string</returns>
    public override string ToString()
    {
        return $"{Street}, {City}, {PostalCode}, {Country}";
    }
}