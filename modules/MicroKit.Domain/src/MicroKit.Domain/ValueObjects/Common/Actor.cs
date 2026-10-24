using MicroKit.Domain.Exceptions;

namespace MicroKit.Domain.ValueObjects.Common;

/// <summary>
/// Identifies who performed an action — a human user, a background job, or a system process.
/// </summary>
/// <remarks>
/// <para>
/// The underlying value is an opaque <see cref="string"/> so that identifiers issued by any
/// identity provider fit without conversion: an OIDC <c>sub</c>, an Entra object id, a
/// Keycloak user id, a service-account name, or a plain GUID rendered as text.
/// </para>
/// <para>
/// Prefer the built-in instances (<see cref="System"/>, <see cref="Anonymous"/>,
/// <see cref="Migration"/>) over a null actor. A named sentinel keeps the audit trail
/// answerable; <see langword="null"/> collapses "unknown", "background job" and "not yet
/// populated" into one indistinguishable value.
/// </para>
/// <para>
/// The built-in instances carry the reserved prefix <see cref="ReservedPrefix"/>, which the
/// public constructor rejects. <see cref="System"/> therefore never equals an actor built from a
/// user whose identifier happens to be <c>system</c>.
/// </para>
/// </remarks>
public sealed record Actor : IValueObject
{
    /// <summary>
    /// The maximum supported length of an actor identifier.
    /// </summary>
    public const int MaxLength = 200;

    /// <summary>
    /// The leading character reserved for the built-in actors.
    /// </summary>
    public const char ReservedPrefix = '$';

    /// <summary>
    /// Initializes a new actor from an identifier issued outside the domain.
    /// </summary>
    /// <param name="value">The actor identifier. Leading and trailing white space is removed.</param>
    /// <exception cref="DomainException">
    /// Thrown when the identifier is empty, exceeds <see cref="MaxLength"/>, or starts with
    /// <see cref="ReservedPrefix"/>.
    /// </exception>
    public Actor(string value) : this(value, allowReserved: false) { }

    private Actor(string value, bool allowReserved)
    {
        Value = Validate(value, allowReserved);
    }

    /// <summary>
    /// Gets the normalized actor identifier.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// The actor used when an action is performed by the application itself,
    /// with no user in the request context (background jobs, schedulers).
    /// </summary>
    public static Actor System { get; } = new("$system", allowReserved: true);

    /// <summary>
    /// The actor used when an action is performed without an authenticated identity.
    /// </summary>
    public static Actor Anonymous { get; } = new("$anonymous", allowReserved: true);

    /// <summary>
    /// The actor used for records created by a data migration rather than by application flow.
    /// </summary>
    public static Actor Migration { get; } = new("$migration", allowReserved: true);

    /// <summary>
    /// Validates and normalizes an actor identifier.
    /// </summary>
    /// <param name="raw">The identifier to validate.</param>
    /// <param name="allowReserved">Whether the identifier may start with <see cref="ReservedPrefix"/>.</param>
    /// <returns>The trimmed identifier.</returns>
    /// <exception cref="DomainException">
    /// Thrown when the identifier is empty, exceeds <see cref="MaxLength"/>, or starts with
    /// <see cref="ReservedPrefix"/> while <paramref name="allowReserved"/> is false.
    /// </exception>
    private static string Validate(string raw, bool allowReserved)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new DomainException("Actor identifier cannot be null or empty.");

        var trimmed = raw.Trim();

        if (trimmed.Length > MaxLength)
            throw new DomainException($"Actor identifier cannot exceed {MaxLength} characters.");

        if (!allowReserved && trimmed[0] == ReservedPrefix)
            throw new DomainException(
                $"Actor identifier cannot start with '{ReservedPrefix}', which is reserved for the built-in actors.");

        return trimmed;
    }

    /// <summary>
    /// Returns the actor identifier.
    /// </summary>
    /// <returns>The actor identifier string.</returns>
    public override string ToString() => Value;
}
