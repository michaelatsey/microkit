namespace MicroKit.Security.Abstractions.Validation;

/// <summary>Contract for high-performance API key validation using <see cref="System.Threading.Tasks.ValueTask"/> and <see cref="System.ReadOnlySpan{T}"/> to minimise allocations.</summary>
public interface IApiKeyValidator
{
    /// <summary>Validates an API key asynchronously.</summary>
    /// <param name="apiKey">The API key as a string.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The validation result, including the principal if the key is valid.</returns>
    ValueTask<ApiKeyValidationResult> ValidateAsync(
        string apiKey,
        CancellationToken cancellationToken = default);

    /// <summary>Validates an API key from a character buffer, avoiding string allocations.</summary>
    /// <param name="apiKey">The API key as a span of characters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The validation result, including the principal if the key is valid.</returns>
    ValueTask<ApiKeyValidationResult> ValidateAsync(
        ReadOnlySpan<char> apiKey,
        CancellationToken cancellationToken = default);

    /// <summary>Validates an API key from a UTF-8 byte buffer for maximum throughput.</summary>
    /// <param name="apiKeyUtf8">The API key encoded as UTF-8 bytes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The validation result, including the principal if the key is valid.</returns>
    ValueTask<ApiKeyValidationResult> ValidateAsync(
        ReadOnlySpan<byte> apiKeyUtf8,
        CancellationToken cancellationToken = default);
}
