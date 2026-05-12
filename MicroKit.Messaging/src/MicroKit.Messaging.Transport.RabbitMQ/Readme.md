# MicroKit.Messaging.Transport.RabbitMQ

RabbitMQ transport adapter for `MicroKit.Messaging`. Provides `RabbitMQOptions` for configuring the broker connection and exchange topology. The transport implementation (`IMessageTransport`) is in active development.

## When to use

Use this package to configure the RabbitMQ connection parameters for the messaging pipeline. When the transport implementation is complete, it will publish outbox messages to a RabbitMQ exchange and consume messages from queues, integrating with the existing `IOutboxPublisher`/`IInboxPublisher` pipeline.

## Installation

```
dotnet add package MicroKit.Messaging.Transport.RabbitMQ
```

## Key types

| Type | Description |
|---|---|
| `RabbitMQOptions` | Connection and exchange configuration for the RabbitMQ broker |

### `RabbitMQOptions` properties

| Property | Default | Description |
|---|---|---|
| `Host` | `"localhost"` | Broker hostname |
| `Port` | `5672` | AMQP port |
| `UserName` | `"guest"` | AMQP username |
| `Password` | `"guest"` | AMQP password |
| `VirtualHost` | `"/"` | RabbitMQ virtual host |
| `ExchangeName` | `"nexus.events"` | Exchange for publishing messages |
| `ExchangeType` | `"topic"` | AMQP exchange type (`topic`, `direct`, `fanout`) |
| `Durable` | `true` | Whether the exchange is declared as durable |
| `PrefetchCount` | `50` | Consumer pre-fetch count |
| `ConnectionTimeout` | `30s` | Maximum time to wait for a connection |

## Usage

```csharp
// Bind configuration from appsettings.json
services.Configure<RabbitMQOptions>(
    configuration.GetSection("MicroKit:Messaging:RabbitMQ"));
```

```json
{
  "MicroKit": {
    "Messaging": {
      "RabbitMQ": {
        "Host": "rabbitmq.internal",
        "ExchangeName": "my-service.events",
        "PrefetchCount": 100
      }
    }
  }
}
```

## Dependencies

- `RabbitMQ.Client`
