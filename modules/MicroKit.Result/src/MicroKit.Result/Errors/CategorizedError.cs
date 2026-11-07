namespace MicroKit.Result;

/// <summary>
/// An error whose category is given explicitly, built by the factories of <see cref="Error"/>
/// (<see cref="Error.NotFound(ErrorCode, string)"/>, <see cref="Error.Conflict(ErrorCode, string)"/>…).
/// </summary>
/// <remarks>
/// For boundaries and ad-hoc errors. A domain error stays one type each (<c>UserNotFoundError</c>), which keeps
/// strong typing in pattern matching (ADR-RESULT-001 D3). Two instances with the same code, message and category
/// are equal.
/// </remarks>
/// <param name="Code">The error code.</param>
/// <param name="Message">The client-safe message.</param>
/// <param name="Category">The kind of failure.</param>
/// <exception cref="ArgumentException">
/// Thrown when <paramref name="Code"/> is uninitialized or <paramref name="Message"/> is empty or whitespace.
/// </exception>
/// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="Category"/> is not a defined value.</exception>
public sealed record CategorizedError(ErrorCode Code, string Message, ErrorCategory Category)
    : Error(RequireCode(Code), RequireMessage(Message))
{
    /// <summary>Gets the kind of failure given at construction.</summary>
    public override ErrorCategory Category { get; } = Enum.IsDefined(Category)
        ? Category
        : throw new ArgumentOutOfRangeException(nameof(Category), Category, "The category is not a defined ErrorCategory value.");

    private static ErrorCode RequireCode(ErrorCode code) =>
        string.IsNullOrWhiteSpace(code.Value)
            ? throw new ArgumentException("The error code is uninitialized; build it with ErrorCode.From.", nameof(code))
            : code;

    private static string RequireMessage(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return message;
    }
}
