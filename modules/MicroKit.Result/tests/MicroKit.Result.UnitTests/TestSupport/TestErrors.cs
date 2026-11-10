namespace MicroKit.Result.UnitTests.TestSupport;

// Shared errors for every test class. Category is overridden where the test needs a specific one;
// TestError keeps the default (Technical) on purpose.
internal sealed record TestError(string Detail = "test")
    : Error(ErrorCode.From("TEST.ERROR"), $"Test error: {Detail}");

internal sealed record OtherError()
    : Error(ErrorCode.From("TEST.OTHER"), "Other error")
{
    public override ErrorCategory Category => ErrorCategory.Conflict;
}

internal sealed record NotFoundTestError(int Id)
    : Error(ErrorCode.From("TEST.NOT_FOUND"), $"Item {Id} not found")
{
    public override ErrorCategory Category => ErrorCategory.NotFound;
}

internal sealed record CriticalTestError()
    : Error(ErrorCode.From("TEST.CRITICAL"), "Critical error")
{
    public override ErrorCategory Category => ErrorCategory.Unavailable;

    public override ErrorSeverity Severity => ErrorSeverity.Critical;

    public override IReadOnlyDictionary<string, object?> Metadata =>
        ErrorMetadata.Create("retryAfter", 30);
}

internal sealed record InformationTestError(string Detail = "info")
    : Error(ErrorCode.From("TEST.INFORMATION"), $"Information: {Detail}")
{
    public override ErrorCategory Category => ErrorCategory.BusinessRule;

    public override ErrorSeverity Severity => ErrorSeverity.Information;
}
