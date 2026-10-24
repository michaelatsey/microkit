using System.Text.Json.Serialization;
using MicroKit.Domain.Exceptions;
using MicroKit.Domain.ValueObjects.Common.Json;

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
/// <para>
/// <c>System.Text.Json</c> writes an actor as its identifier, a JSON string. With default options it
/// reads it back through the public constructor: JSON is treated as untrusted, so a value with the
/// reserved prefix fails as a <see cref="System.Text.Json.JsonException"/> and a built-in actor does
/// <b>not</b> round-trip. Storage the application wrote itself (an outbox, an event store, a cache)
/// opts into rehydrating built-in actors by registering
/// <see cref="Json.TrustedActorJsonConverter"/> in <c>JsonSerializerOptions.Converters</c>.
/// </para>
/// <para>
/// Until the Messaging outbox can register that converter (#174), an event that must carry a
/// built-in actor should carry the actor's <see cref="Value"/> as a string, and rebuild the actor
/// with <see cref="Restore(string)"/> where it is consumed.
/// </para>
/// </remarks>
[JsonConverter(typeof(ActorJsonConverter))]
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
    /// Restores an actor from an identifier read back from storage or a message, built-in actors
    /// included.
    /// </summary>
    /// <param name="value">The stored identifier, as <see cref="Value"/> returned it.</param>
    /// <returns>
    /// The built-in instance when <paramref name="value"/> is the value of <see cref="System"/>,
    /// <see cref="Anonymous"/> or <see cref="Migration"/>; otherwise a new actor built by the public
    /// constructor.
    /// </returns>
    /// <exception cref="DomainException">
    /// Thrown when the identifier is empty, too long, or starts with <see cref="ReservedPrefix"/>
    /// without being a built-in actor.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This only rehydrates a value this domain persisted earlier, in a value converter or a
    /// deserializer reading back its own data: the constructor rejects the reserved prefix, so it
    /// cannot read back a stored built-in actor.
    /// </para>
    /// <para>
    /// Never pass user input, a request header or a token claim to this method. It turns the
    /// reserved values into the built-in actors, so a caller supplying <c>$system</c> would be
    /// recorded as the application itself. Build an actor from external input with the
    /// constructor, which rejects the reserved prefix.
    /// </para>
    /// <para>
    /// The default <c>System.Text.Json</c> converter of <see cref="Actor"/> does not call this
    /// method, so a built-in actor does not round-trip with default options.
    /// <see cref="Json.TrustedActorJsonConverter"/> calls it, under the same rule: register that
    /// converter only for storage the application wrote itself, never for a request body.
    /// </para>
    /// </remarks>
    public static Actor Restore(string value) => value switch
    {
        "$system" => System,
        "$anonymous" => Anonymous,
        "$migration" => Migration,
        _ => new Actor(value),
    };

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
