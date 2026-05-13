namespace MicroKit.Security.Abstractions.Enums;

/// <summary>
/// Supported hashing algorithms for storing API keys.
/// </summary>
public enum ApiKeyHashAlgorithms
{
    /// <summary>
    /// SHA-256 — excellent balance of performance and security.
    /// </summary>
    SHA256 = 0,

    /// <summary>
    /// SHA-512 — slower but more collision-resistant and harder to brute-force on 64-bit processors.
    /// </summary>
    SHA512 = 1
}
