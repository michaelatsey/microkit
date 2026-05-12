using Azure.Messaging;
using MediatR;
using MicroKit.Abstractions.Serialization;
using MicroKit.Messaging.Abstractions.Outbox;
using MicroKit.MultiTenancy.Abstractions;
using MicroKit.Sample.OrderApi.Application.Abstractions;
using MicroKit.Sample.OrderApi.Domain.Entities;
using MicroKit.Sample.OrderApi.Domain.Events;
using MicroKit.Sample.OrderApi.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MicroKit.Sample.OrderApi.Application.CreateOrder;

/// <summary>Handles the <see cref="CreateOrderCommand"/> by persisting the order and enqueuing an outbox message atomically.</summary>
public class CreateOrderHandler : ICommandHandler<CreateOrderCommand, Order>
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IOutboxService _outboxService;
    private readonly IMicroKitSerializer _serializer;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="context">The application EF Core database context.</param>
    /// <param name="outboxService">Service for enqueuing outbox messages.</param>
    /// <param name="serializer">Serializer for domain events.</param>
    /// <param name="tenantContext">Provides the current tenant identity.</param>
    public CreateOrderHandler(ApplicationDbContext context, IOutboxService outboxService, IMicroKitSerializer serializer, ITenantContext tenantContext)
    {
        _dbContext = context;
        _outboxService = outboxService;
        _serializer = serializer;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc/>
    public async Task<Order> Handle(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        var order = Order.Create(command.ProductId, 19.99m) 
            ?? throw new InvalidOperationException("Failed to create order.");

        // On prépare l'événement de domaine (IDomainEvent)
        var orderCreated = new OrderCreatedEvent(order.Id, "client@email.com");

        await ExecuteAsync(async ctx =>
        {
            _dbContext.Orders.Add(order);

            // Action 2: Enqueue Outbox (dans la même transaction)
            // L'Expert utilise le contexte actuel pour la traçabilité
            await _outboxService.EnqueueAsync(
                tenantId: _tenantContext.Tenant!.Id, // On utilise l'ID résolu
                messageId: orderCreated.Id.ToString(),
                payload: _serializer.Serialize(orderCreated),
                destination: new OutboxDestination { PublishAsNotification = true },
                // On peut générer un nouveau CorrelationId ici s'il n'existe pas encore
                correlationId: Guid.NewGuid().ToString(),
                idempotencyKey: $"create_order_{order.Id}",
                cancellationToken: ctx);

            await _dbContext.SaveChangesAsync(ctx);
            // Ne pas retourner de valeur ici, car le délégué doit retourner Task, pas Task<T>
        }, cancellationToken);

        return order;
    }

    /// <summary>Executes an operation within a database transaction, using the current transaction if one is already active.</summary>
    /// <param name="operation">The async operation to execute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            if (_dbContext.Database.CurrentTransaction != null)
            {
                await operation(cancellationToken);
                return;
            }

            await using var transaction =
                await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                await operation(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }
}
