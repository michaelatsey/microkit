//using MicroKit.Abstractions.Contexts;

//namespace MicroKit.Security.Contexts;

///// <summary>
///// Represents the current client context resolved from HTTP headers.
///// Scoped lifetime - unique per request.
///// </summary>
//public sealed class ClientContext : IClientContext
//{
//    /// <summary>
//    /// The unique identifier of the client (from X-Restaurant-Id header).
//    /// </summary>
//    public string ClientId { get; private set; } = string.Empty;

//    /// <summary>
//    /// Indicates whether the client context has been resolved.
//    /// </summary>
//    public bool IsResolved { get; private set; }

//    /// <summary>
//    /// Sets the client identifier. Can only be set once per request.
//    /// </summary>
//    /// <param name="clientId">The client identifier.</param>
//    /// <exception cref="InvalidOperationException">Thrown if client is already set.</exception>
//    public void SetClient(string clientId)
//    {
//        if (IsResolved)
//        {
//            throw new InvalidOperationException("Client context has already been resolved for this request.");
//        }

//        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        
//        ClientId = clientId;
//        IsResolved = true;
//    }

//    /// <summary>
//    /// Ensures the client context has been resolved.
//    /// </summary>
//    /// <exception cref="InvalidOperationException">Thrown if client is not resolved.</exception>
//    public void EnsureResolved()
//    {
//        if (!IsResolved)
//        {
//            throw new InvalidOperationException("Client context has not been resolved.");
//        }
//    }
//}
