namespace MicroKit.Result;

/// <summary>
/// Represents a validation error associated with a specific property.
/// </summary>
public interface IValidationError : IError
{
    /// <summary>Gets the name of the property that failed validation.</summary>
    string PropertyName { get; }
}
