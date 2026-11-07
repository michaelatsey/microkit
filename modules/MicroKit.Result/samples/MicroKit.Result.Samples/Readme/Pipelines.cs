using Microsoft.Extensions.Logging;

namespace MicroKit.Result.Samples.Readme;

// README "Pipelines": its C# blocks are here verbatim, modulo indentation; the rest only makes them build. Keep them identical to README.md.
internal static class PipelinesSamples
{
    internal static void MapSample(Guid id)
    {
        Result<string> name = GetUser(id).Map(u => u.Name);
    }

    internal static void BindSample(Guid id)
    {
        Result<Invoice> invoice = GetUser(id)
            .Bind(u => GetCart(u.CartId))
            .Bind(c => CreateInvoice(c));
    }

    internal static void MatchSample(Result<int> result)
    {
        string label = result.Match(
            onSuccess: v => $"Got {v}",
            onFailure: e => $"Error: {e.Message}");
    }

    internal static void TapSample(Result<int> result, ILogger logger, IMetrics metrics)
    {
        Result<int> observed = result
            .Tap(v => logger.ValueLoaded(v))
            .TapError(e => metrics.RecordFailure(e.Code));
    }

    internal static void EnsureSample(Guid id)
    {
        Result<User> active = GetUser(id)
            .Ensure(u => u.IsActive, new UserInactiveError(id))
            .Ensure(u => u.IsEmailVerified, new EmailNotVerifiedError());
    }

    internal static void EnsureLazySample(Guid id)
    {
        Result<User> activeLazy = GetUser(id)
            .Bind(u => u.IsActive ? Result.Success(u) : Result.Failure<User>(new UserInactiveError(id)));
    }

    internal static void MapErrorSample(Guid id, string correlationId)
    {
        Result<Order> result = GetOrder(id)
            .MapError(e => new EnrichedError(e, correlationId));
    }

    internal static void CompensateSample(Guid id)
    {
        Result<Product> product = GetProduct(id)
            .Compensate(_ => GetDefaultProduct());
    }

    internal static void ValueAccessSample(Result<int> result)
    {
        int value = result.GetValueOrDefault(0);

        int strict = result.GetValueOrThrow();                          // ResultException on failure
        int custom = result.GetValueOrThrow(e => new MyException(e));  // custom exception

        if (result.TryGetValue(out var v))
            Process(v);
    }

    internal static void FinallySample(Result<int> result, ILogger logger)
    {
        Result<int> completed = result.Finally(r => logger.Completed(r.IsSuccess));
    }

    internal static void ConversionSample(Result<int> result)
    {
        Result nonGeneric = result.ToResult();  // drops the value, keeps the error
    }

    internal sealed record User(string Name, Guid CartId, bool IsActive, bool IsEmailVerified);

    internal sealed record Cart;

    internal sealed record Invoice;

    internal sealed record Order;

    internal sealed record Product;

    internal sealed record UserInactiveError(Guid UserId) : Error(ErrorCode.From("USER.INACTIVE"), $"User {UserId} is inactive")
    {
        public override ErrorCategory Category => ErrorCategory.BusinessRule;
    }

    internal sealed record EmailNotVerifiedError() : Error(ErrorCode.From("USER.EMAIL_NOT_VERIFIED"), "Email is not verified")
    {
        public override ErrorCategory Category => ErrorCategory.BusinessRule;
    }

    // A wrapper forwards the wrapped error's category, severity and metadata; without them it answers 500.
    // The correlation id joins the forwarded metadata, which stays in process: ProblemDetails does not emit it.
    internal sealed record EnrichedError(IError Inner, string CorrelationId) : Error(Inner.Code, Inner.Message)
    {
        public override ErrorCategory Category => Inner.Category;

        public override ErrorSeverity Severity => Inner.Severity;

        public override IReadOnlyDictionary<string, object?> Metadata =>
            new ErrorMetadataBuilder().Merge(Inner.Metadata).WithCorrelationId(CorrelationId).Build();
    }

    internal sealed class MyException(IError error) : Exception(error.Message);

    internal interface IMetrics
    {
        void RecordFailure(ErrorCode code);
    }

    private static Result<User> GetUser(Guid id) => throw new NotSupportedException();

    private static Result<Cart> GetCart(Guid cartId) => throw new NotSupportedException();

    private static Result<Invoice> CreateInvoice(Cart cart) => throw new NotSupportedException();

    private static Result<Order> GetOrder(Guid id) => throw new NotSupportedException();

    private static Result<Product> GetProduct(Guid id) => throw new NotSupportedException();

    private static Result<Product> GetDefaultProduct() => throw new NotSupportedException();

    private static void Process(int value) { }
}

internal static partial class PipelineLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded value {Value}")]
    public static partial void ValueLoaded(this ILogger logger, int value);

    [LoggerMessage(Level = LogLevel.Information, Message = "Completed, success: {IsSuccess}")]
    public static partial void Completed(this ILogger logger, bool isSuccess);
}
