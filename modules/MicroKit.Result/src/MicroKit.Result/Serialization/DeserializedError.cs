namespace MicroKit.Result.Serialization;

/// <summary>
/// An error read back from JSON by the result converters, whose original type is not known: it keeps the code,
/// message, category, severity and the metadata the payload carried.
/// </summary>
/// <remarks>Metadata values are the JSON primitives (<see cref="string"/>, <see cref="long"/>, <see cref="double"/>,
/// <see cref="bool"/>, <see langword="null"/>), or a <see cref="System.Text.Json.JsonElement"/> for an object or an array.</remarks>
/// <param name="Code">The error code.</param>
/// <param name="Message">The message.</param>
/// <param name="Category">The kind of failure.</param>
/// <param name="Severity">The severity.</param>
/// <param name="Metadata">The metadata the payload carried; empty when it carried none.</param>
public sealed record DeserializedError(
    ErrorCode Code,
    string Message,
    ErrorCategory Category,
    ErrorSeverity Severity,
    IReadOnlyDictionary<string, object?> Metadata) : Error(Code, Message)
{
    /// <inheritdoc/>
    public override ErrorCategory Category { get; } = Category;

    /// <inheritdoc/>
    public override ErrorSeverity Severity { get; } = Severity;

    /// <inheritdoc/>
    public override IReadOnlyDictionary<string, object?> Metadata { get; } = Metadata;
}

/// <summary>
/// A validation error read back from JSON by the result converters. Like <see cref="DeserializedError"/>, a separate
/// type, it keeps <see cref="IValidationError.PropertyName"/>, so HTTP still groups it by field.
/// </summary>
/// <param name="Code">The error code.</param>
/// <param name="Message">The message.</param>
/// <param name="Category">The kind of failure.</param>
/// <param name="Severity">The severity.</param>
/// <param name="Metadata">The metadata the payload carried; empty when it carried none.</param>
/// <param name="PropertyName">The name of the property that failed validation.</param>
public sealed record DeserializedValidationError(
    ErrorCode Code,
    string Message,
    ErrorCategory Category,
    ErrorSeverity Severity,
    IReadOnlyDictionary<string, object?> Metadata,
    string PropertyName) : Error(Code, Message), IValidationError
{
    /// <inheritdoc/>
    public override ErrorCategory Category { get; } = Category;

    /// <inheritdoc/>
    public override ErrorSeverity Severity { get; } = Severity;

    /// <inheritdoc/>
    public override IReadOnlyDictionary<string, object?> Metadata { get; } = Metadata;
}
