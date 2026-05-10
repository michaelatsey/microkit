namespace MicroKit.Domain.Exceptions;

/// <summary>
/// Exception levée lorsqu'une entité n'est pas trouvée.
/// </summary>
/// <seealso cref="Exception" />
public class NotFoundException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NotFoundException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public NotFoundException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="NotFoundException"/> class.
    /// </summary>
    /// <param name="entityName">Name of the entity.</param>
    /// <param name="key">The key.</param>
    public NotFoundException(string entityName, object key)
        : base($"{entityName} avec l'ID '{key}' est introuvable.") { }
}