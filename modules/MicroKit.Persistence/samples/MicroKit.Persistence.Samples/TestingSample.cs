namespace MicroKit.Persistence.Samples;

public static class TestingSample
{
    // README: "Testing without a database"
    public static async Task RunAsync()
    {
        var orders = new InMemoryRepository<Order, OrderId>();
        var unitOfWork = new InMemoryUnitOfWork();
        var clock = TimeProvider.System;
        var order = Order.Place("SO-1", new Actor("user-1"), clock);

        await orders.AddAsync(order);
        (await orders.FindAsync(order.Id))!.AddLine("BOOK", 1, new Actor("user-1"), clock);
        await unitOfWork.CommitAsync();

        if (unitOfWork.CommitCount != 1 || orders.All.Single().Lines.Count != 1)
            throw new InvalidOperationException("The in-memory doubles did not behave as documented.");
    }
}
