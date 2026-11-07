using System.Text.Json.Serialization;

namespace MicroKit.Result;

/// <summary>
/// The error that <see cref="Result.Try(Func{Result})"/>, <see cref="Result{T}.Try(Func{T})"/> and their
/// async counterparts build, without a mapper, from a caught exception.
/// </summary>
/// <remarks>
/// <see cref="Error.Message"/> is generic and safe for a client: the exception's own message, which may hold
/// internal details (SQL, paths, connection strings), never becomes the error's message.
/// <see cref="Exception"/> keeps the exception in process, for logging; it is not serialized.
/// </remarks>
/// <param name="Exception">The caught exception, for logging only.</param>
public sealed record ExceptionError(Exception Exception)
    : Error(ErrorCode.Exception, "An unexpected error occurred.")
{
    /// <summary>Gets the caught exception, kept in process for logging. It is not serialized.</summary>
    [JsonIgnore]
    public Exception Exception { get; } = Exception ?? throw new ArgumentNullException(nameof(Exception));

    /// <inheritdoc/>
    public override ErrorCategory Category => ErrorCategory.Technical;

    /// <summary>
    /// Prints <see cref="IError.Code"/>, <see cref="Error.Message"/> and <see cref="Category"/>, never
    /// <see cref="Exception"/>: the generated <c>ToString</c> would otherwise carry the exception's message and
    /// stack trace, the details the generic message hides. Log the exception through <see cref="Exception"/>.
    /// </summary>
    /// <param name="builder">The builder the record's <c>ToString</c> writes to.</param>
    /// <returns><see langword="true"/>: members were written.</returns>
    protected override bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("Code = ").Append(Code.Value).Append(", Message = ").Append(Message).Append(", Category = ").Append(Category);
        return true;
    }
}
