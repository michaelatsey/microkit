namespace MicroKit.Idempotency.Abstractions.Contracts;

/// <summary>
/// Computes a deterministic hash of a request object for idempotency verification.
/// </summary>
public interface IRequestHasher
{
    /// <summary>
    /// Computes a hash of the given request.
    /// </summary>
    /// <typeparam name="T">Type of the request</typeparam>
    /// <param name="request">The request to hash</param>
    /// <returns>Base64-encoded hash string</returns>
    string ComputeHash<T>(T request);

    /// <summary>
    /// Computes a hash using a custom normalizer function.
    /// </summary>
    /// <typeparam name="T">Type of the request</typeparam>
    /// <param name="request">The request to hash</param>
    /// <param name="normalizer">Function that produces the canonical string representation</param>
    /// <returns>Base64-encoded hash string</returns>
    string ComputeHash<T>(T request, Func<T, string> normalizer);
}
