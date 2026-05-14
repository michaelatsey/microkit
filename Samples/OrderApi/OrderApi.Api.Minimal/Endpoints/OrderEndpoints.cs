using MicroKit.Cqrs.Abstractions.Commands;
using MicroKit.Cqrs.Abstractions.Queries;
using MicroKit.MultiTenancy.Abstractions;
using OrderApi.Application.Orders.Commands;
using OrderApi.Application.Orders.Dtos;
using OrderApi.Application.Orders.Queries;

namespace OrderApi.Api.Minimal.Endpoints;

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/api/orders");

        orders.MapPost("/", PlaceOrder);
        orders.MapPut("/{id:guid}/confirm", ConfirmOrder);
        orders.MapPut("/{id:guid}/cancel", CancelOrder);
        orders.MapGet("/{id:guid}", GetOrderById);
        orders.MapGet("/customer/{customerId}", ListOrdersByCustomer);

        return app;
    }

    private static async Task<IResult> PlaceOrder(
        PlaceOrderRequest request,
        ICommandBus bus,
        ITenant tenant,
        CancellationToken ct)
    {
        var id = Guid.NewGuid();
        ICommand<Guid> cmd = new PlaceOrderCommand(
            id,
            tenant.Id ?? "default",
            request.CustomerId,
            request.Items,
            request.IdempotencyKey ?? id.ToString());

        var orderId = await bus.SendAsync(cmd, ct);
        return Results.Created($"/api/orders/{orderId}", new { id = orderId });
    }

    private static async Task<IResult> ConfirmOrder(
        Guid id,
        ICommandBus bus,
        ITenant tenant,
        CancellationToken ct)
    {
        ICommand<bool> cmd = new ConfirmOrderCommand(id, tenant.Id ?? "default", $"confirm-{id}");
        var success = await bus.SendAsync(cmd, ct);
        return success ? Results.Ok() : Results.NotFound();
    }

    private static async Task<IResult> CancelOrder(
        Guid id,
        ICommandBus bus,
        ITenant tenant,
        CancellationToken ct)
    {
        ICommand<bool> cmd = new CancelOrderCommand(id, tenant.Id ?? "default");
        var success = await bus.SendAsync(cmd, ct);
        return success ? Results.Ok() : Results.NotFound();
    }

    private static async Task<IResult> GetOrderById(
        Guid id,
        IQueryBus bus,
        ITenant tenant,
        CancellationToken ct)
    {
        var query = new GetOrderByIdQuery(id, tenant.Id ?? "default");
        var dto = await bus.AskAsync(query, ct);
        return dto is null ? Results.NotFound() : Results.Ok(dto);
    }

    private static async Task<IResult> ListOrdersByCustomer(
        string customerId,
        IQueryBus bus,
        ITenant tenant,
        CancellationToken ct)
    {
        var query = new ListOrdersByCustomerQuery(tenant.Id ?? "default", customerId);
        var results = await bus.AskAsync(query, ct);
        return Results.Ok(results);
    }
}

public sealed record PlaceOrderRequest(string CustomerId, IReadOnlyList<OrderItemDto> Items, string? IdempotencyKey);
