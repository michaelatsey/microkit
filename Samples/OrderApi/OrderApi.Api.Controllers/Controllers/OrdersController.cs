using MicroKit.Cqrs.Abstractions.Commands;
using MicroKit.Cqrs.Abstractions.Queries;
using MicroKit.MultiTenancy.Abstractions;
using Microsoft.AspNetCore.Mvc;
using OrderApi.Application.Orders.Commands;
using OrderApi.Application.Orders.Dtos;
using OrderApi.Application.Orders.Queries;

namespace OrderApi.Api.Controllers.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class OrdersController(ICommandBus commandBus, IQueryBus queryBus, ITenant tenant) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> PlaceOrder([FromBody] PlaceOrderRequest request, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        ICommand<Guid> cmd = new PlaceOrderCommand(
            id,
            tenant.Id ?? "default",
            request.CustomerId,
            request.Items,
            request.IdempotencyKey ?? id.ToString());

        var orderId = await commandBus.SendAsync(cmd, ct);
        return CreatedAtAction(nameof(GetById), new { id = orderId }, new { id = orderId });
    }

    [HttpPut("{id:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid id, CancellationToken ct)
    {
        ICommand<bool> cmd = new ConfirmOrderCommand(id, tenant.Id ?? "default", $"confirm-{id}");
        var success = await commandBus.SendAsync(cmd, ct);
        return success ? Ok() : NotFound();
    }

    [HttpPut("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        ICommand<bool> cmd = new CancelOrderCommand(id, tenant.Id ?? "default");
        var success = await commandBus.SendAsync(cmd, ct);
        return success ? Ok() : NotFound();
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var query = new GetOrderByIdQuery(id, tenant.Id ?? "default");
        var dto = await queryBus.AskAsync(query, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpGet("customer/{customerId}")]
    public async Task<IActionResult> ListByCustomer(string customerId, CancellationToken ct)
    {
        var query = new ListOrdersByCustomerQuery(tenant.Id ?? "default", customerId);
        var results = await queryBus.AskAsync(query, ct);
        return Ok(results);
    }
}

public sealed record PlaceOrderRequest(string CustomerId, IReadOnlyList<OrderItemDto> Items, string? IdempotencyKey);
