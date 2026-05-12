using MediatR;
using MicroKit.Sample.OrderApi.Application.CreateOrder;
using Microsoft.AspNetCore.Mvc;

namespace MicroKit.Sample.OrderApi.Controllers;

/// <summary>API controller for order operations.</summary>
[ApiController]
[Route("[controller]")]
public class OrdersController : ControllerBase
{

    private readonly ISender _mediator;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="mediator">The MediatR sender.</param>
    public OrdersController(ISender mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Creates a new order.</summary>
    /// <param name="request">The order creation request.</param>
    /// <returns>The created order ID.</returns>
    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
    {
        CreateOrderCommand comand = new (request.ProductId, request.Amount);

        var order = await _mediator.Send(comand);

        return Ok(new { OrderId = order.Id, Message = "Order created, Outbox message persisted." });
    }
}
