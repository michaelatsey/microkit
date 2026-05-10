namespace MicroKit.Domain.Exceptions;

/// <summary>
/// Rerepresente une erreur de validation individuelle.
/// </summary>
/// <seealso cref="Exception" />
public class ForbiddenException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ForbiddenException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public ForbiddenException(string message) : base(message)
    {
    }
}
