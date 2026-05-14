using Microsoft.EntityFrameworkCore;
using OrderApi.Domain.Orders;
using OrderApi.Domain.Orders.Repositories;

namespace OrderApi.Infrastructure.Persistence.Repositories;

public sealed class OrderRepository(AppDbContext context) : IOrderRepository
{
    public Task<Order?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => context.Orders.FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<IReadOnlyCollection<Order>> GetByCustomerIdAsync(string tenantId, string customerId, CancellationToken ct = default)
    {
        var results = await context.Orders
            .Where(o => o.TenantId == tenantId && o.CustomerId == customerId)
            .ToListAsync(ct);
        return results;
    }

    public async Task AddAsync(Order order, CancellationToken ct = default)
        => await context.Orders.AddAsync(order, ct);

    public Task UpdateAsync(Order order, CancellationToken ct = default)
    {
        context.Orders.Update(order);
        return Task.CompletedTask;
    }
}
