namespace MicroKit.Persistence.Samples;

public sealed class AddOrderLine(
    ITransactionalContext transactions,
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    // README: "A command without MediatR"
    public Task HandleAsync(OrderId id, string sku, int quantity, Actor by, CancellationToken ct = default) =>
        transactions.ExecuteAsync(
            static async (s, token) =>
            {
                var order = await s.Orders.FindAsync(s.Id, token).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Order {s.Id} does not exist.");

                order.AddLine(s.Sku, s.Quantity, s.By, s.Clock);
                await s.UnitOfWork.CommitAsync(token).ConfigureAwait(false);
            },
            (Orders: orders, UnitOfWork: unitOfWork, Clock: clock, Id: id, Sku: sku, Quantity: quantity, By: by),
            ct);
}
