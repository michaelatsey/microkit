
using System.Security.Cryptography;

namespace MicroKit.Security.Core.Utilities;
/// <summary>
/// Secure token generation utilities.
/// </summary>
public static class SecureTokenGenerator
{
    private const string Base62Chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private const string Base64UrlChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

    /// <summary>
    /// Generates a cryptographically secure random token.
    /// </summary>
    /// <param name="length">Token length in bytes.</param>
    /// <returns>Base64URL encoded token.</returns>
    public static string GenerateToken(int length = 32)
    {
        Span<byte> bytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    /// <summary>
    /// Generates a token using Base62 encoding (alphanumeric only).
    /// </summary>
    /// <param name="length">Desired output length.</param>
    /// <returns>Base62 encoded token.</returns>
    public static string GenerateBase62Token(int length = 32)
    {
        Span<byte> bytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(bytes);

        return string.Create(length, bytes.ToArray(), (chars, state) =>
        {
            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = Base62Chars[state[i] % Base62Chars.Length];
            }
        });
    }

    /// <summary>
    /// Generates an API key with a prefix.
    /// </summary>
    /// <param name="prefix">Key prefix (e.g., "mk_live_", "mk_test_").</param>
    /// <param name="length">Random part length.</param>
    /// <returns>Prefixed API key.</returns>
    public static string GenerateApiKey(string prefix = "mk_", int length = 32)
    {
        return $"{prefix}{GenerateBase62Token(length)}";
    }

    /// <summary>
    /// Generates a refresh token.
    /// </summary>
    /// <param name="length">Token length in bytes.</param>
    /// <returns>Refresh token.</returns>
    public static string GenerateRefreshToken(int length = 64)
    {
        return GenerateToken(length);
    }

    /// <summary>
    /// Generates a correlation ID.
    /// </summary>
    /// <returns>Correlation ID in format: timestamp-random.</returns>
    public static string GenerateCorrelationId()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var random = GenerateBase62Token(8);
        return $"{timestamp:x}-{random}";
    }
}
