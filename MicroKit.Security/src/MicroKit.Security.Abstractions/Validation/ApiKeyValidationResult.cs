using MicroKit.Security.Abstractions.Enums;
using MicroKit.Security.Abstractions.Identity;

namespace MicroKit.Security.Abstractions.Validation;

/// <summary>
/// Immutable result of an API key validation attempt.
/// </summary>
public sealed record ApiKeyValidationResult
{
    /// <summary>
    /// Validation status.
    /// </summary>
    public required ValidationStatus Status { get; init; }

    /// <summary>
    /// Security principal associated with the key, or null when validation failed.
    /// </summary>
    public required ISecurityPrincipal? Principal { get; init; }

    /// <summary>
    /// Error message when validation fails.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Additional metadata associated with the validation result.
    /// </summary>
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Indicates whether validation succeeded.
    /// </summary>
    public bool IsValid => Status == ValidationStatus.Valid && Principal is not null;

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    /// <param name="principal">The authenticated principal.</param>
    /// <returns>Success result.</returns>
    public static ApiKeyValidationResult Success(ISecurityPrincipal principal) => new()
    {
        Status = ValidationStatus.Valid,
        Principal = principal
    };

    /// <summary>
    /// Creates a successful validation result with metadata.
    /// </summary>
    /// <param name="principal">The authenticated principal.</param>
    /// <param name="metadata">Additional metadata.</param>
    /// <returns>Success result with metadata.</returns>
    public static ApiKeyValidationResult Success(
        ISecurityPrincipal principal,
        IReadOnlyDictionary<string, object> metadata) => new()
        {
            Status = ValidationStatus.Valid,
            Principal = principal,
            Metadata = metadata
        };

    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    /// <param name="status">The failure status.</param>
    /// <param name="message">Optional error message.</param>
    /// <returns>Failure result.</returns>
    public static ApiKeyValidationResult Failure(ValidationStatus status, string? message = null) => new()
    {
        Status = status,
        Principal = null,
        ErrorMessage = message
    };

    /// <summary>
    /// Creates a result for an invalid key.
    /// </summary>
    /// <param name="message">Optional error message.</param>
    /// <returns>Invalid result.</returns>
    public static ApiKeyValidationResult Invalid(string? message = null) =>
        Failure(ValidationStatus.Invalid, message ?? "The API key is invalid.");

    /// <summary>
    /// Creates a result for an expired key.
    /// </summary>
    /// <param name="message">Optional error message.</param>
    /// <returns>Expired result.</returns>
    public static ApiKeyValidationResult Expired(string? message = null) =>
        Failure(ValidationStatus.Expired, message ?? "The API key has expired.");

    /// <summary>
    /// Creates a result for a revoked key.
    /// </summary>
    /// <param name="message">Optional error message.</param>
    /// <returns>Revoked result.</returns>
    public static ApiKeyValidationResult Revoked(string? message = null) =>
        Failure(ValidationStatus.Revoked, message ?? "The API key has been revoked.");

    /// <summary>
    /// Creates a result for a rate-limited key.
    /// </summary>
    /// <param name="message">Optional error message.</param>
    /// <returns>Rate-limited result.</returns>
    public static ApiKeyValidationResult RateLimited(string? message = null) =>
        Failure(ValidationStatus.RateLimited, message ?? "Rate limit exceeded.");
}
