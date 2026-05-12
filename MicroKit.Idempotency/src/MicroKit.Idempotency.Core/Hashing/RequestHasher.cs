using MicroKit.Abstractions.Serialization;
using MicroKit.Idempotency.Abstractions.Contracts;
using System.Security.Cryptography;
using System.Text;

namespace MicroKit.Idempotency.Core.Hashing;

/// <summary>Computes SHA-256 hashes of request objects to detect duplicate submissions.</summary>
public sealed class RequestHasher : IRequestHasher
{
    private readonly IMicroKitSerializer _serializer;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="serializer">Serializer used to normalize requests before hashing.</param>
    public RequestHasher(IMicroKitSerializer serializer)
    {
        _serializer = serializer;
    }

    /// <summary>
    /// Computes a SHA-256 hash of the request object
    /// </summary>
    /// <typeparam name="T">Type of the request</typeparam>
    /// <param name="request">The request to hash</param>
    /// <returns>Base64 encoded hash string</returns>
    public string ComputeHash<T>(T request)
    {
        var json = _serializer.Serialize(request);
        var bytes = Encoding.UTF8.GetBytes(json);
        var hash = SHA256.HashData(bytes);

        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Computes a hash with a custom normalizer
    /// </summary>
    public string ComputeHash<T>(T request, Func<T, string> normalizer)
    {
        var normalized = normalizer(request);
        var bytes = Encoding.UTF8.GetBytes(normalized);
        var hash = SHA256.HashData(bytes);

        return Convert.ToBase64String(hash);
    }
}
