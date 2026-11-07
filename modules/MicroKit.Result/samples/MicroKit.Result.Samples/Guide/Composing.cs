using Microsoft.Extensions.Logging;

namespace MicroKit.Result.Samples.Guide;

// Backs docs/guides/usage.md, "Compose results": its C# blocks are here verbatim, modulo indentation; the rest only
// makes them build. Keep the two identical.
internal static class ComposingSamples
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

    internal static void EnsureSample(Guid id)
    {
        Result<User> active = GetUser(id)
            .Ensure(u => u.IsActive, new UserInactiveError(id))
            .Ensure(u => u.IsEmailVerified, new EmailNotVerifiedError());
    }

    internal static void EnsureLazySample(Guid id)
    {
        Result<User> activeLazy = GetUser(id)
            .Bind(u => u.IsActive ? Result.Success(u) : new UserInactiveError(u.Id));
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

    internal static void FailureRailSample(Guid id, string correlationId)
    {
        // MapError: replace the error, stay on the failure rail
        Result<Order> enriched = GetOrder(id)
            .MapError(e => EnrichedError.Enrich(e, correlationId));

        // Compensate: return to the success rail with a fallback
        Result<Product> product = GetProduct(id)
            .Compensate(_ => GetDefaultProduct());
    }

    // A wrapper forwards the wrapped error's category, severity and metadata. It wraps a single error:
    // Enrich rebuilds a collection from its errors, each enriched, nested collections included
    internal sealed record EnrichedError(IError Inner, string CorrelationId) : Error(Inner.Code, Inner.Message)
    {
        public override ErrorCategory Category => Inner.Category;

        public override ErrorSeverity Severity => Inner.Severity;

        public override IReadOnlyDictionary<string, object?> Metadata =>
            new ErrorMetadataBuilder().Merge(Inner.Metadata).WithCorrelationId(CorrelationId).Build();

        public static IError Enrich(IError error, string correlationId) => error is ErrorCollection collection
            ? ErrorCollection.From(collection.Select(e => Enrich(e, correlationId)))
            : new EnrichedError(error, correlationId);
    }

    internal static string DescribeSample(Guid id)
    {
        Result<User> result = GetUser(id);
        if (!result.TryGetValue(out User? user))
            return $"Unavailable: {result.Error.Message}";

        return $"{user.Name}, active: {user.IsActive}";
    }

    internal static void FallbackSample(Guid id)
    {
        string label = GetUser(id).Map(u => u.Name).GetValueOrDefault("Guest");
    }

    internal static void FinallySample(Result<int> result, ILogger logger)
    {
        Result<int> completed = result.Finally(r => logger.Completed(r.IsSuccess));
    }

    internal static void CommandSample(Order order, IMetrics metrics)
    {
        Result shipped = CheckAddress(order)
            .Ensure(() => order.IsPaid, new OrderNotPaidError(order.Id))
            .Bind(() => Ship(order))
            .TapError(e => metrics.RecordFailure(e.Code));

        string status = shipped.Match(
            onSuccess: () => "Shipped",
            onFailure: e => e.Message);

        // From a value to a command
        Result archived = GetOrder(order.Id).Bind(o => Archive(o));
        Result checkedOnly = GetOrder(order.Id).ToResult(); // keeps the error, drops the value
    }

    internal static void LinqSample(Guid id)
    {
        Result<OrderDto> dto =
            from user in GetUser(id)
            from cart in GetCart(user.CartId)
            from order in CreateOrder(cart)
            select order.ToDto();

        // where needs an error for the false case: use Where, the method form
        Result<int> positive = Result.Success(42)
            .Where(v => v > 0, new NegativeValueError());

        // AsEnumerable: one element on success, none on failure
        foreach (OrderDto value in dto.AsEnumerable())
            Process(value);
    }

    internal sealed record User(Guid Id, string Name, Guid CartId, bool IsActive, bool IsEmailVerified);

    internal sealed record Cart;

    internal sealed record Invoice;

    internal sealed record Order(Guid Id, bool IsPaid)
    {
        public OrderDto ToDto() => new(Id);
    }

    internal sealed record OrderDto(Guid Id);

    internal sealed record Product;

    internal sealed record UserInactiveError(Guid UserId) : Error(ErrorCode.From("USER.INACTIVE"), $"User {UserId} is inactive")
    {
        public override ErrorCategory Category => ErrorCategory.BusinessRule;
    }

    internal sealed record EmailNotVerifiedError() : Error(ErrorCode.From("USER.EMAIL_NOT_VERIFIED"), "Email is not verified")
    {
        public override ErrorCategory Category => ErrorCategory.BusinessRule;
    }

    internal sealed record OrderNotPaidError(Guid OrderId) : Error(ErrorCode.From("ORDER.NOT_PAID"), $"Order {OrderId} is not paid")
    {
        public override ErrorCategory Category => ErrorCategory.BusinessRule;
    }

    internal sealed record NegativeValueError() : Error(ErrorCode.From("VALUE.NEGATIVE"), "Value must be positive")
    {
        public override ErrorCategory Category => ErrorCategory.Validation;
    }

    internal interface IMetrics
    {
        void RecordFailure(ErrorCode code);
    }

    private static Result<User> GetUser(Guid id) => throw new NotSupportedException();

    private static Result<Cart> GetCart(Guid cartId) => throw new NotSupportedException();

    private static Result<Invoice> CreateInvoice(Cart cart) => throw new NotSupportedException();

    private static Result<Order> CreateOrder(Cart cart) => throw new NotSupportedException();

    private static Result<Order> GetOrder(Guid id) => throw new NotSupportedException();

    private static Result<Product> GetProduct(Guid id) => throw new NotSupportedException();

    private static Result<Product> GetDefaultProduct() => throw new NotSupportedException();

    private static Result CheckAddress(Order order) => throw new NotSupportedException();

    private static Result Ship(Order order) => throw new NotSupportedException();

    private static Result Archive(Order order) => throw new NotSupportedException();

    private static void Process(OrderDto dto) { }
}

internal static partial class PipelineLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded value {Value}")]
    public static partial void ValueLoaded(this ILogger logger, int value);

    [LoggerMessage(Level = LogLevel.Information, Message = "Completed, success: {IsSuccess}")]
    public static partial void Completed(this ILogger logger, bool isSuccess);
}
