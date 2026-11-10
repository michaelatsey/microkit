using MicroKit.Persistence.Testing.PostgreSql;

namespace MicroKit.Persistence.Samples;

public static class RelationalTestingSample
{
    // README: "Testing against a real database"
    public static async Task<bool> RunAsync()
    {
        if (!DockerProbe.IsAvailable)
            return false; // in a test: skip, the test framework's way

        await using var postgres = new PostgreSqlFixture();
        await postgres.StartAsync();

        // One database per test, on the shared server.
        var connectionString = postgres.GetConnectionString("orders_" + Guid.NewGuid().ToString("N"));
        await using var services = new ServiceCollection().AddShopPersistence(connectionString).BuildServiceProvider();

        await using (var scope = services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ShopDbContext>().Database.EnsureCreatedAsync();
            var orders = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
            await orders.AddAsync(Order.Place("SO-1", new Actor("user-1"), TimeProvider.System));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }

        await using (var scope = services.CreateAsyncScope())
        {
            var found = await scope.ServiceProvider.GetRequiredService<IOrderRepository>().FindByNumberAsync("SO-1");
            if (found is null)
                throw new InvalidOperationException("The order did not round-trip through PostgreSQL.");
        }

        return true;
    }
}
