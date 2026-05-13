namespace MicroKit.Security.Abstractions.Enums;

/// <summary>
/// Possible statuses for an authentication validation attempt.
/// </summary>
public enum ValidationStatus : byte
{
    /// <summary>Unknown or undetermined status.</summary>
    Unknown = 0,

    /// <summary>Validation succeeded.</summary>
    Valid = 1,

    /// <summary>The token or key has expired.</summary>
    Expired = 2,

    /// <summary>The token or key has been revoked.</summary>
    Revoked = 3,

    /// <summary>The token or key is invalid.</summary>
    Invalid = 4,

    /// <summary>Rate limit exceeded.</summary>
    RateLimited = 5
}
