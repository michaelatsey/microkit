namespace MicroKit.Domain.Exceptions;

/// <summary>Base class for all domain-layer exceptions.</summary>
public class DomainException : Exception
{
    /// <summary>Gets the stable, machine-readable error code for this exception.</summary>
    public string Code { get; }

    /// <summary>Initializes a new instance with a default error code.</summary>
    /// <param name="message">Human-readable error message.</param>
    public DomainException(string message) : base(message)
    {
        Code = "DOMAIN_ERROR";
    }

    /// <summary>Initializes a new instance with an explicit error code.</summary>
    /// <param name="code">Machine-readable error code.</param>
    /// <param name="message">Human-readable error message.</param>
    public DomainException(string code, string message) : base(message)
    {
        Code = code;
    }

    /// <summary>Initializes a new instance wrapping an inner exception.</summary>
    /// <param name="message">Human-readable error message.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public DomainException(string message, Exception innerException) : base(message, innerException)
    {
        Code = "DOMAIN_ERROR";
    }
}
