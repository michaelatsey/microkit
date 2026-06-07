namespace MicroKit.Auth;

/// <summary>
/// Represents the outcome of an authorization decision — either authorized or denied.
/// Use the static factory methods <see cref="Authorized()"/> and
/// <see cref="Denied(string)"/> to construct instances.
/// </summary>
/// <remarks>
/// <c>AuthorizationResult</c> is a pure decision type. It does not map directly to an
/// HTTP status code; callers are responsible for translating it to an
/// <c>IActionResult</c> or a <see cref="Result{T}"/> failure as needed.
/// </remarks>
public sealed record AuthorizationResult
{
    private AuthorizationResult() { }

    /// <summary>Gets a value indicating whether the authorization was granted.</summary>
    public bool IsAuthorized { get; private init; }

    /// <summary>
    /// Gets an optional human-readable reason for the decision.
    /// Always populated on denial; <see langword="null"/> on success.
    /// </summary>
    public string? Reason { get; private init; }

    /// <summary>
    /// Gets the <see cref="Permission"/> that was checked, if available.
    /// <see langword="null"/> when the decision is not permission-specific (e.g. a role check).
    /// </summary>
    public Permission? CheckedPermission { get; private init; }

    // ── Static factories ──────────────────────────────────────────────────

    /// <summary>
    /// Creates an authorized result with no denial reason.
    /// </summary>
    /// <returns>An <see cref="AuthorizationResult"/> representing a successful authorization.</returns>
    public static AuthorizationResult Authorized() =>
        new() { IsAuthorized = true };

    /// <summary>
    /// Creates a denied result with a human-readable reason.
    /// </summary>
    /// <param name="reason">The reason access was denied. Must not be null or whitespace.</param>
    /// <returns>An <see cref="AuthorizationResult"/> representing a denied authorization.</returns>
    public static AuthorizationResult Denied(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new() { IsAuthorized = false, Reason = reason };
    }

    /// <summary>
    /// Creates a denied result that records which specific <see cref="Permission"/> was checked.
    /// </summary>
    /// <param name="permission">The permission that was evaluated and denied.</param>
    /// <param name="reason">The reason access was denied. Must not be null or whitespace.</param>
    /// <returns>An <see cref="AuthorizationResult"/> representing a denied authorization for a specific permission.</returns>
    public static AuthorizationResult Denied(Permission permission, string reason)
    {
        ArgumentNullException.ThrowIfNull(permission);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new() { IsAuthorized = false, Reason = reason, CheckedPermission = permission };
    }
}
