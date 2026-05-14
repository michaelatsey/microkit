# OrderApi Sample

A complete sample application demonstrating Hexagonal Architecture + DDD + CQRS using the MicroKit library suite.

## Architecture

```
OrderApi.Domain          — Aggregate roots, domain events, value objects, repository interfaces
OrderApi.Application     — Commands, queries, handlers, DTOs (application use cases)
OrderApi.Infrastructure  — EF Core, repositories, MicroKit wiring (DI registration)
OrderApi.Api.Minimal     — Minimal API entrypoint (ASP.NET Core)
OrderApi.Api.Controllers — Controllers-based entrypoint (ASP.NET Core MVC)
```

### Domain model

- `Order` aggregate root with `OrderItem` value objects and `Money` value object
- Domain events: `OrderPlacedEvent`, `OrderConfirmedEvent`
- Repository interface: `IOrderRepository`

### MicroKit features demonstrated

| Feature | Package |
|---|---|
| CQRS (commands + queries) | `MicroKit.Cqrs.Abstractions`, `MicroKit.Cqrs.MediatR` |
| Idempotency | `MicroKit.Idempotency.Core`, `MicroKit.Idempotency.MediatR`, `MicroKit.Idempotency.EFCore` |
| Outbox pattern | `MicroKit.Messaging.Core`, `MicroKit.Messaging.Persistence.EFCore`, `MicroKit.Messaging.Publisher.MediatR` |
| Distributed caching | `MicroKit.Caching.Distributed` |
| Multi-tenancy | `MicroKit.MultiTenancy.Extensions` (header strategy) |
| Resilience | `MicroKit.Resilience`, `MicroKit.Resilience.MediatR` |

## Prerequisites

- .NET 10 SDK
- Docker (for PostgreSQL)

## Running

```bash
# Start PostgreSQL
docker-compose up -d

# Run the Minimal API
dotnet run --project OrderApi.Api.Minimal --configfile ../nuget.config

# Or run the Controllers API
dotnet run --project OrderApi.Api.Controllers --configfile ../nuget.config
```

## API Endpoints

| Method | Path | Description |
|---|---|---|
| POST | `/api/orders` | Place a new order |
| PUT | `/api/orders/{id}/confirm` | Confirm an order |
| PUT | `/api/orders/{id}/cancel` | Cancel an order |
| GET | `/api/orders/{id}` | Get order by ID |
| GET | `/api/orders/customer/{customerId}` | List orders by customer |

### Headers

- `X-Tenant-Id: <tenant-id>` — required for tenant resolution

### Example request

```json
POST /api/orders
X-Tenant-Id: tenant-1

{
  "customerId": "customer-123",
  "idempotencyKey": "order-unique-key-001",
  "items": [
    {
      "productId": "prod-001",
      "productName": "Widget",
      "quantity": 2,
      "unitPrice": 9.99,
      "currency": "USD"
    }
  ]
}
```
