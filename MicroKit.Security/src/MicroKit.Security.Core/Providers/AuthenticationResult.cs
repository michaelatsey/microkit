
using MicroKit.Security.Abstractions.Enums;
using MicroKit.Security.Abstractions.Identity;

namespace MicroKit.Security.Core.Providers;
/// <summary>
/// Result of an authentication attempt.
/// </summary>
public sealed record AuthenticationResult
{
    /// <summary>
    /// Whether authentication was successful.
    /// </summary>
    public required bool IsSuccess { get; init; }

    /// <summary>
    /// Authenticated principal (null if failed).
    /// </summary>
    public ISecurityPrincipal? Principal { get; init; }

    /// <summary>
    /// Validation status.
    /// </summary>
    public ValidationStatus Status { get; init; } = ValidationStatus.Unknown;

    /// <summary>
    /// Error message if authentication failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Additional metadata from the authentication process.
    /// </summary>
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Token expiration time if applicable.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// Creates a successful authentication result.
    /// </summary>
    public static AuthenticationResult Success(
        ISecurityPrincipal principal,
        DateTimeOffset? expiresAt = null,
        IReadOnlyDictionary<string, object>? metadata = null) => new()
        {
            IsSuccess = true,
            Principal = principal,
            Status = ValidationStatus.Valid,
            ExpiresAt = expiresAt,
            Metadata = metadata
        };

    /// <summary>
    /// Creates a failed authentication result.
    /// </summary>
    public static AuthenticationResult Failure(
        ValidationStatus status,
        string? errorMessage = null) => new()
        {
            IsSuccess = false,
            Principal = null,
            Status = status,
            ErrorMessage = errorMessage
        };

    /// <summary>
    /// Creates an expired token result.
    /// </summary>
    public static AuthenticationResult Expired(string? message = null) =>
        Failure(ValidationStatus.Expired, message ?? "Token has expired");

    /// <summary>
    /// Creates an invalid credentials result.
    /// </summary>
    public static AuthenticationResult Invalid(string? message = null) =>
        Failure(ValidationStatus.Invalid, message ?? "Invalid credentials");

    /// <summary>
    /// Creates a revoked credentials result.
    /// </summary>
    public static AuthenticationResult Revoked(string? message = null) =>
        Failure(ValidationStatus.Revoked, message ?? "Credentials have been revoked");
}
