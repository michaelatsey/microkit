
//using MicroKit.Abstractions.Contexts;

//namespace MicroKit.Security.Contexts;

///// <summary>
///// Represents the correlation context for distributed tracing.
///// Scoped lifetime - unique per request.
///// </summary>
//public sealed class CorrelationContext : ICorrelationContext
//{
//    /// <summary>
//    /// The correlation identifier for the current request.
//    /// </summary>
//    public string CorrelationId { get; private set; } = string.Empty;

//    /// <summary>
//    /// Indicates whether the correlation context has been initialized.
//    /// </summary>
//    public bool IsInitialized { get; private set; }

//    /// <summary>
//    /// Initializes the correlation context with an identifier.
//    /// </summary>
//    /// <param name="correlationId">The correlation identifier.</param>
//    public void Initialize(string correlationId)
//    {
//        if (IsInitialized)
//        {
//            return; // Already initialized, ignore subsequent calls
//        }

//        CorrelationId = string.IsNullOrWhiteSpace(correlationId) 
//            ? Guid.NewGuid().ToString("N") 
//            : correlationId;
        
//        IsInitialized = true;
//    }

//    /// <summary>
//    /// Gets the correlation ID, generating one if not yet initialized.
//    /// </summary>
//    public string GetOrCreate()
//    {
//        if (!IsInitialized)
//        {
//            Initialize(Guid.NewGuid().ToString("N"));
//        }
        
//        return CorrelationId;
//    }
//}
