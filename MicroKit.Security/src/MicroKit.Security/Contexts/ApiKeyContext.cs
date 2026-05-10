//using MicroKit.Abstractions.Contexts;

//namespace MicroKit.Security.Contexts;

///// <summary>
///// Represents the current API key context resolved from HTTP headers.
///// Scoped lifetime - unique per request.
///// </summary>
//public sealed class ApiKeyContext : IApiKeyContext
//{
//    /// <summary>
//    /// The API key (from X-Api-Key header).
//    /// </summary>
//    public string ApiKey { get; private set; } = string.Empty;

//    /// <summary>
//    /// The identifier associated with the validated API key.
//    /// </summary>
//    public string KeyIdentifier { get; private set; } = string.Empty;

//    /// <summary>
//    /// Indicates whether the API key context has been resolved.
//    /// </summary>
//    public bool IsResolved { get; private set; }

//    /// <summary>
//    /// Indicates whether the API key has been validated.
//    /// </summary>
//    public bool IsValidated { get; private set; }

//    /// <summary>
//    /// Sets the API key. Can only be set once per request.
//    /// </summary>
//    /// <param name="apiKey">The API key.</param>
//    /// <exception cref="InvalidOperationException">Thrown if API key is already set.</exception>
//    public void SetApiKey(string apiKey)
//    {
//        if (IsResolved)
//        {
//            throw new InvalidOperationException("API key context has already been resolved for this request.");
//        }

//        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        
//        ApiKey = apiKey;
//        IsResolved = true;
//    }

//    /// <summary>
//    /// Marks the API key as validated with its identifier.
//    /// </summary>
//    /// <param name="keyIdentifier">The identifier associated with the API key.</param>
//    public void MarkValidated(string keyIdentifier)
//    {
//        ArgumentException.ThrowIfNullOrWhiteSpace(keyIdentifier);
        
//        KeyIdentifier = keyIdentifier;
//        IsValidated = true;
//    }

//    /// <summary>
//    /// Ensures the API key context has been resolved.
//    /// </summary>
//    /// <exception cref="InvalidOperationException">Thrown if API key is not resolved.</exception>
//    public void EnsureResolved()
//    {
//        if (!IsResolved)
//        {
//            throw new InvalidOperationException("API key context has not been resolved.");
//        }
//    }
//}
