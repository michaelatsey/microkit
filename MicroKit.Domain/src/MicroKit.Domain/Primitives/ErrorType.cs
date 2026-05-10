namespace MicroKit.Domain.Primitives;

/// <summary>Classifies the nature of a domain operation failure.</summary>
public enum ErrorType
{
    /// <summary>No error.</summary>
    None = 0,
    /// <summary>A general, unclassified failure.</summary>
    Failure = 1,
    /// <summary>The requested resource was not found.</summary>
    NotFound = 2,
    /// <summary>A conflict with existing state.</summary>
    Conflict = 3,
    /// <summary>One or more input values failed validation.</summary>
    Validation = 4,
    /// <summary>The caller is not authenticated.</summary>
    Unauthorized = 5,
    /// <summary>The caller is authenticated but not permitted to perform the operation.</summary>
    Forbidden = 6,
}
