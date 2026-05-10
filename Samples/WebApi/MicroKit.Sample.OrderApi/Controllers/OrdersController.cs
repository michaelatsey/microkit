using MediatR;
using MicroKit.Sample.OrderApi.Application.CreateOrder;
using Microsoft.AspNetCore.Mvc;

namespace MicroKit.Sample.OrderApi.Controllers;

[ApiController]
[Route("[controller]")]
public class OrdersController : ControllerBase
{

    private readonly ISender _mediator;

    public OrdersController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
    {
        CreateOrderCommand comand = new (request.ProductId, request.Amount);

        var order = await _mediator.Send(comand);

        return Ok(new { OrderId = order.Id, Message = "Order created, Outbox message persisted." });
    }
}
