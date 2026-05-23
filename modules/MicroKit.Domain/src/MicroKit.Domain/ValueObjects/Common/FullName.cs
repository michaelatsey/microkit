using MicroKit.Domain.Exceptions;

namespace MicroKit.Domain.ValueObjects.Common;

/// <summary>
/// Represents a person's full name with first and last name components.
/// Provides a normalized display name and validation for required components.
/// </summary>
/// <param name="FirstName">The person's first name</param>
/// <param name="LastName">The person's last name</param>
public sealed record FullName(string FirstName, string LastName)
{
    /// <summary>
    /// Gets the person's first name.
    /// </summary>
    public string FirstName { get; } = ValidateNameComponent(FirstName, nameof(FirstName));

    /// <summary>
    /// Gets the person's last name.
    /// </summary>
    public string LastName { get; } = ValidateNameComponent(LastName, nameof(LastName));

    /// <summary>
    /// Gets the formatted display name in "FirstName LastName" format.
    /// </summary>
    public string DisplayName => $"{FirstName} {LastName}";

    /// <summary>
    /// Validates a name component (first name or last name).
    /// </summary>
    /// <param name="nameComponent">The name component to validate</param>
    /// <param name="componentName">The name of the component for error messages</param>
    /// <returns>The trimmed and validated name component</returns>
    /// <exception cref="DomainException">Thrown when the name component is null or empty</exception>
    private static string ValidateNameComponent(string nameComponent, string componentName)
    {
        if (string.IsNullOrWhiteSpace(nameComponent))
            throw new DomainException($"{componentName} cannot be null or empty.");

        var trimmed = nameComponent.Trim();

        if (trimmed.Length == 0)
            throw new DomainException($"{componentName} cannot be empty after trimming.");

        return trimmed;
    }

    /// <summary>
    /// Creates a new FullName instance.
    /// </summary>
    /// <param name="firstName">The person's first name</param>
    /// <param name="lastName">The person's last name</param>
    /// <returns>A new FullName instance</returns>
    /// <exception cref="DomainException">Thrown when any name component is null or empty</exception>
    public static FullName Create(string firstName, string lastName)
    {
        return new FullName(firstName, lastName);
    }

    /// <summary>
    /// Returns the display name representation of this full name.
    /// </summary>
    /// <returns>The formatted display name</returns>
    public override string ToString() => DisplayName;
}