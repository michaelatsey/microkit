namespace MicroKit.Result.Samples.Guide;

// Backs docs/guides/usage.md, "Define errors": its C# blocks are here verbatim, modulo indentation; the rest only
// makes them build. Keep the two identical.
internal static class DefiningErrorsSamples
{
    public sealed record UserNotFoundError(Guid UserId)
        : Error(ErrorCode.From("USER.NOT_FOUND"), $"User {UserId} not found")
    {
        public override ErrorCategory Category => ErrorCategory.NotFound;
    }

    internal static Result<Order> ErrorFactoriesSample(Guid orderId)
    {
        // An ad hoc error with an explicit category: one factory per ErrorCategory
        CategorizedError missing = Error.NotFound(ErrorCode.From("ORDER.NOT_FOUND"), $"Order {orderId} does not exist.");
        return missing; // the implicit conversion makes it a failure
    }

    internal sealed record Order;

    internal static void ErrorCodeSample()
    {
        var code = ErrorCode.From("ORDER.PAYMENT.DECLINED");
        string raw = code; // implicit conversion to string

        // Predefined convenience codes, for an error without a domain code
        ErrorCode notFound = ErrorCode.NotFound;               // "NOT_FOUND"
        ErrorCode timeout = ErrorCode.Timeout;                 // "TIMEOUT"
        ErrorCode external = ErrorCode.ExternalServiceFailure; // "EXTERNAL_SERVICE_FAILURE"

        // Ordinal comparison
        bool ordered = ErrorCode.Conflict < ErrorCode.NotFound;
    }

    public sealed record PaymentDeclinedError(Guid PaymentId, string ProviderReference)
        : Error(ErrorCode.From("PAYMENT.CARD.DECLINED"), "The card was declined.")
    {
        public override ErrorCategory Category => ErrorCategory.BusinessRule;

        public override ErrorSeverity Severity => ErrorSeverity.Warning;

        // In-process context for logs and traces; no response writes it by default
        public override IReadOnlyDictionary<string, object?> Metadata =>
            new ErrorMetadataBuilder()
                .Add("paymentId", PaymentId)
                .Add("providerReference", ProviderReference)
                .WithTraceId()
                .Build();
    }

    internal static void ErrorExtensionsSample(IError error)
    {
        bool isValidation = error.IsCategory(ErrorCategory.Validation);
        bool isCritical = error.IsCritical();
        bool isWarning = error.IsWarning();
        bool hasPayment = error.HasMetadata("paymentId");
        string? reference = error.TryGetMetadata<string>("providerReference");
    }
}
