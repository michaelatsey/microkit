namespace MicroKit.Result;

/// <summary>
/// Internal error type wrapping caught exceptions from Try and TryAsync factory methods.
/// </summary>
internal sealed record ExceptionError(Exception Exception)
    : Error(ErrorCode.From("SYSTEM.EXCEPTION"), Exception.Message)
{
    /// <inheritdoc/>
    public override ErrorCategory Category => ErrorCategory.Technical;

    /// <inheritdoc/>
    public override IReadOnlyDictionary<string, object?> Metadata =>
        ErrorMetadata.Create("exceptionType", Exception.GetType().FullName);
}
