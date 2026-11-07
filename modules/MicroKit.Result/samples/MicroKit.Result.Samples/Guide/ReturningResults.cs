namespace MicroKit.Result.Samples.Guide;

// Backs docs/guides/usage.md, "Return a result": its C# blocks are here verbatim, modulo indentation; the rest only
// makes them build. Keep the two identical.
internal static class ReturningResultsSamples
{
    internal static void FactoriesSample(Guid orderId)
    {
        Result saved = Result.Success();
        Result rejected = Result.Failure(new OrderLockedError(orderId));

        Result<int> count = Result.Success(42);
        Result<int> missing = Result.Failure<int>(new OrderLockedError(orderId));

        // A value converts to a success, an error to a failure
        Result<int> fromValue = 42;
        Result<int> fromError = new OrderLockedError(orderId);
        Result commandFailed = new OrderLockedError(orderId);

        // Unit stands for "no value" where a type argument is required
        Result<Unit> acknowledged = Result.Success(Unit.Value);
    }

    internal sealed class OrderBook(Dictionary<Guid, Order> orders)
    {
        private readonly Dictionary<Guid, Order> _orders = orders;

        public Result<Order> FindOrder(Guid id) =>
            _orders.TryGetValue(id, out Order? order)
                ? Result.Success(order)
                : Error.NotFound(ErrorCode.From("ORDER.NOT_FOUND"), $"Order {id} does not exist.");

        public Result Cancel(Order order)
        {
            if (order.IsShipped)
                return new OrderLockedError(order.Id);

            _orders.Remove(order.Id);
            return Result.Success();
        }
    }

    internal sealed record Order(Guid Id, bool IsShipped);

    internal sealed record OrderLockedError(Guid OrderId)
        : Error(ErrorCode.From("ORDER.LOCKED"), $"Order {OrderId} can no longer change.")
    {
        public override ErrorCategory Category => ErrorCategory.Conflict;
    }
}
