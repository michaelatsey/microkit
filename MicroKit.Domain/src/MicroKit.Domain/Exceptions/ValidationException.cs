namespace MicroKit.Domain.Exceptions;

/// <summary>
/// Exception levée en cas d'erreurs de validation.
/// </summary>
/// <seealso cref="DomainException" />
public class ValidationException : DomainException
{
    /// <summary>
    /// Gets the errors.
    /// </summary>
    /// <value>
    /// The errors.
    /// </value>
    public IReadOnlyList<ValidationError> Errors { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationException"/> class.
    /// </summary>
    /// <param name="errors">The errors.</param>
    public ValidationException(IEnumerable<ValidationError> errors)
        : base("Erreur(s) de validation détectée(s).")
    {
        Errors = errors.ToList().AsReadOnly();
    }
}

/// <summary>
/// Représente une erreur de validation individuelle.
/// </summary>
/// <seealso cref="IEquatable&lt;ValidationError&gt;" />
public record ValidationError(string PropertyName, string ErrorMessage);

