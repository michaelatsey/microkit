using Microsoft.AspNetCore.Http;

namespace MicroKit.Result.Samples.Readme;

// README "Error System": its C# blocks are here verbatim, modulo indentation; the rest only makes them build. Keep them identical to README.md.
internal static class ErrorSystemSamples
{
    // "IError / Error" declares a type, so it sits at class level rather than in a method.
    // Define domain-specific typed errors
    public sealed record UserNotFoundError(Guid UserId)
        : Error(ErrorCode.From("USER.NOT_FOUND"), $"User {UserId} not found")
    {
        public override ErrorCategory Category => ErrorCategory.NotFound;
    }

    internal static void ErrorCodeSample()
    {
        var code = ErrorCode.From("ORDER.PAYMENT.DECLINED");
        string raw = code;   // implicit string conversion

        // Predefined constants
        ErrorCode notFound = ErrorCode.NotFound;                // "NOT_FOUND"
        ErrorCode validation = ErrorCode.Validation;              // "VALIDATION"
        ErrorCode timeout = ErrorCode.Timeout;                 // "TIMEOUT"
        ErrorCode external = ErrorCode.ExternalServiceFailure;  // "EXTERNAL_SERVICE_FAILURE"
                                                                // ...19 predefined constants total

        // Comparable
        bool ordered = ErrorCode.Conflict < ErrorCode.NotFound;   // ordinal comparison
    }

    internal static void ErrorMetadataBuilderSample(HttpContext httpContext, string userId)
    {
        IReadOnlyDictionary<string, object?> meta = new ErrorMetadataBuilder()
            .WithTimestamp()
            .WithCorrelationId(httpContext.TraceIdentifier)
            .WithTraceId()           // Activity.Current?.TraceId
            .Add("userId", userId)
            .Build();
    }

    internal static void ErrorCollectionSample(IError error1, IError error2, IError error3)
    {
        var collection = ErrorCollection.From(error1, error2, error3);

        // Filter: a filter may match nothing, so it returns a list, never an empty collection
        IReadOnlyList<IError> validation = collection.WithCategory(ErrorCategory.Validation);
        IReadOnlyList<IError> critical = collection.WithSeverity(ErrorSeverity.Critical);
        IReadOnlyList<IError> notFound = collection.WithCode(ErrorCode.NotFound);
        IReadOnlyList<ValidationError> typed = collection.OfType<ValidationError>();
        bool hasValidation = collection.HasCategory(ErrorCategory.Validation);

        // Group
        IReadOnlyDictionary<ErrorCategory, IReadOnlyList<IError>> byCategory =
            collection.GroupByCategory();

        // Flatten nested collections
        ErrorCollection flat = collection.Flatten();
    }

    internal static void ErrorExtensionsSample(IError error)
    {
        bool isValidation = error.IsCategory(ErrorCategory.Validation);
        bool isCritical = error.IsCritical();
        bool isWarning = error.IsWarning();
        bool isInformation = error.IsInformation();
        bool hasCorrelationId = error.HasMetadata("correlationId");
        string? correlationId = error.TryGetMetadata<string>("correlationId");
        ResultException thrown = error.ToException();   // wraps in ResultException
    }
}
