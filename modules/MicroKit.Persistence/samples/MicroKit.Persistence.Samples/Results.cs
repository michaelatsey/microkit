namespace MicroKit.Persistence.Samples;

using MicroKit.Result;

public sealed class PlaceOrder(
    IResultUnitOfWork transactions,
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    // README: "Persistence failures as Result"
    public ValueTask<Result<OrderId>> HandleAsync(string number, Actor by, CancellationToken ct = default) =>
        transactions.TryExecuteAsync(
            static async (s, token) =>
            {
                if (string.IsNullOrWhiteSpace(s.Number))
                    return Result.Failure<OrderId>(Error.Validation(ErrorCode.From("ORDER.NUMBER.EMPTY"), "An order needs a number."));

                var order = Order.Place(s.Number, s.By, s.Clock);
                await s.Orders.AddAsync(order, token).ConfigureAwait(false);
                await s.UnitOfWork.CommitAsync(token).ConfigureAwait(false);
                return Result.Success(order.Id);
            },
            (Orders: orders, UnitOfWork: unitOfWork, Clock: clock, Number: number, By: by),
            ct);
}

public static class ResultTestingSample
{
    // README: "Persistence failures as Result" — the in-memory double
    public static async Task RunAsync()
    {
        var orders = new InMemoryRepository<Order, OrderId>();
        var unitOfWork = new InMemoryUnitOfWork();
        var order = Order.Place("SO-1", new Actor("user-1"), TimeProvider.System);
        await orders.AddAsync(order);
        orders.SetVersion(order, 2); // another command changed it

        var result = await unitOfWork.TryExecuteAsync(
            static async (s, ct) =>
            {
                var stale = await s.Orders.FindAsync(s.Id, expectedVersion: 1, ct);
                stale!.AddLine("BOOK", 1, new Actor("user-1"), TimeProvider.System);
                await s.UnitOfWork.CommitAsync(ct);
                return Result.Success();
            },
            (Orders: orders, UnitOfWork: unitOfWork, order.Id));

        // result.Error.Code == "PERSISTENCE.ORDER.CONCURRENCY", category Conflict; no commit counted.
        if (result.Error.Code.Value != "PERSISTENCE.ORDER.CONCURRENCY" || unitOfWork.CommitCount != 0)
            throw new InvalidOperationException("The in-memory Result path did not behave as documented.");
    }
}
