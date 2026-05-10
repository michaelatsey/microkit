namespace MicroKit.Domain.Primitives;

/// <summary>Represents a domain error with a stable code and a human-readable message.</summary>
public sealed class Error : IEquatable<Error>
{
    /// <summary>The sentinel value that represents the absence of an error.</summary>
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.None);

    /// <summary>Gets the stable, machine-readable error code (e.g. "Order.NotFound").</summary>
    public string Code { get; }

    /// <summary>Gets the human-readable description of the error.</summary>
    public string Message { get; }

    /// <summary>Gets the category that classifies this error.</summary>
    public ErrorType Type { get; }

    private Error(string code, string message, ErrorType type)
    {
        Code = code;
        Message = message;
        Type = type;
    }

    /// <summary>Creates a general failure error.</summary>
    public static Error Failure(string code, string message) =>
        new(code, message, ErrorType.Failure);

    /// <summary>Creates a not-found error.</summary>
    public static Error NotFound(string code, string message) =>
        new(code, message, ErrorType.NotFound);

    /// <summary>Creates a conflict error.</summary>
    public static Error Conflict(string code, string message) =>
        new(code, message, ErrorType.Conflict);

    /// <summary>Creates a validation error.</summary>
    public static Error Validation(string code, string message) =>
        new(code, message, ErrorType.Validation);

    /// <summary>Creates an unauthorized error.</summary>
    public static Error Unauthorized(string code, string message) =>
        new(code, message, ErrorType.Unauthorized);

    /// <summary>Creates a forbidden error.</summary>
    public static Error Forbidden(string code, string message) =>
        new(code, message, ErrorType.Forbidden);

    /// <inheritdoc/>
    public bool Equals(Error? other) =>
        other is not null && Code == other.Code && Type == other.Type;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Error other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Code, Type);

    /// <inheritdoc/>
    public override string ToString() => string.IsNullOrEmpty(Code) ? "None" : $"{Code}: {Message}";

    public static bool operator ==(Error left, Error right) => left.Equals(right);
    public static bool operator !=(Error left, Error right) => !left.Equals(right);
}
