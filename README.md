# RabbitMQ.Services

A .NET library that simplifies integrating RabbitMQ into ASP.NET Core applications. It provides dependency injection extensions, a consumer hosted service, an outbox pattern implementation, and a URI-based endpoint configuration model.

## Features

- URI-based RabbitMQ endpoint configuration
- Consumer hosted service with automatic retry and delay support
- Outbox pattern for reliable message delivery via Entity Framework Core
- DNS-based cluster resolution
- OpenTelemetry activity propagation

## Requirements

- .NET 9 or .NET 10
- RabbitMQ.Client 7.x
- Microsoft.EntityFrameworkCore.Relational (for the outbox pattern)

## Installation

```
dotnet add package RabbitMQ.Services
```

## Getting Started

### 1. Register Core Services

Call `AddRabbitMQ` in your `Program.cs` or `Startup.cs`:

```csharp
services.AddRabbitMQ();
```

This registers all internal infrastructure services (connection factory, endpoint parser, DNS resolver, etc.).

---

### 2. Consuming Messages

#### Define a Message

```csharp
public class OrderCreatedMessage : BaseMessage
{
    public int OrderId { get; set; }
}
```

`BaseMessage` provides `TraceId` and `SpanId` properties used for distributed tracing.

#### Implement a Handler

Extend `BaseMessageHandler<T>` and override `HandleAsync`:

```csharp
public class OrderCreatedHandler : BaseMessageHandler<OrderCreatedMessage>
{
    public override async Task HandleAsync(OrderCreatedMessage message, CancellationToken token)
    {
        // process the message
    }
}
```

Override `HandleAsync(T message, int retryAttempt, CancellationToken token)` or the full overload with `delayAttempt` if you need retry/delay context.

#### Register the Consumer Hosted Service

```csharp
services.AddConsumerHostedService<OrderCreatedMessage, OrderCreatedHandler>(configuration);
```

#### Configuration (appsettings.json)

```json
{
  "RabbitMQ": {
    "Consumers": {
      "Defaults": {
        "MaxRetryCount": 3
      },
      "OrderCreatedMessage": {
        "Url": "amqp://user:password@localhost/orders/order.created"
      }
    }
  }
}
```

The section name under `Consumers` must match the **message type name** (`typeof(TMessage).Name`).

You can also configure the consumer programmatically using the optional `configureOptions` action:

```csharp
services.AddConsumerHostedService<OrderCreatedMessage, OrderCreatedHandler>(
    configuration,
    config =>
    {
        config.Url = "amqp://user:password@localhost/orders/order.created";
        config.MaxRetryCount = 5;
    });
```

#### Consumer Configuration Options

| Property | Default | Description |
|---|---|---|
| `Url` | *(required)* | RabbitMQ endpoint URI |
| `ConnectionName` | Handler full type name | AMQP connection name |
| `MaxRetryCount` | `3` | Number of retry attempts before dead-lettering |
| `MaxDelays` | `null` | Maximum number of delay attempts |
| `DelayInSeconds` | `null` | Delay between retry attempts in seconds |
| `BindQueue` | `true` | Whether to declare and bind the queue on startup |

#### Delaying Message Processing

Throw `DelayMessageException` from a handler to reschedule the message:

```csharp
throw new DelayMessageException(TimeSpan.FromSeconds(30), "Not ready yet");
```

---

### 3. Sending Messages (Outbox Pattern)

The outbox pattern stores messages in your database within the same transaction as your business data, guaranteeing at-least-once delivery.

#### Implement `IOutboxDbContext`

Your `DbContext` must implement `IOutboxDbContext`:

```csharp
public class AppDbContext : DbContext, IOutboxDbContext
{
    // EF Core already provides Set<T>() and SaveChangesAsync
}
```

#### Add the Outbox Table

In `OnModelCreating`, call:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.AddOutboxEntities("outbox_messages");
    // optionally specify a schema: modelBuilder.AddOutboxEntities("outbox_messages", "myschema");
}
```

#### Register Outbox Services

```csharp
services.AddOutboxServices<AppDbContext>(configuration);
```

#### Configuration (appsettings.json)

```json
{
  "RabbitMQ": {
    "Outbox": {
      "ConnectionName": "my-service-producer",
      "Namespace": "my-service"
    }
  }
}
```

#### Send a Message

Inject `IAsyncMessageSender` and call `SendMessageAsync`:

```csharp
public class OrderService(IAsyncMessageSender sender)
{
    public async Task CreateOrderAsync()
    {
        // ... business logic ...

        await sender.SendMessageAsync(
            "amqp://user:password@localhost/orders/order.created",
            new OrderCreatedMessage { OrderId = 42 });

        // Commit both business data and outbox message in one transaction
        await dbContext.SaveChangesAsync();
    }
}
```

#### Deliver Outbox Messages

Inject `IMessageDeliveryService` and call `SendMessagesAsync` periodically (e.g., from a background service or a scheduled job):

```csharp
await deliveryService.SendMessagesAsync();
```

---

## RabbitMQ Endpoint URI Format

Endpoints are specified as URIs with the `amqp://` or `amqp://` scheme:

```
amqp://[user:password@]host[:port]/[vhost/]queue[?param=value&...]
```

### Examples

```
amqp://localhost/myqueue
amqp://user:pass@localhost/myvhost/myqueue
amqp://localhost/myqueue?exchangetype=direct&route=my.routing.key
amqp://localhost/myqueue?consumers=4&prefetch=10
amqp://localhost/myqueue?temporary=true
amqp://localhost/myqueue?hosts=host1:5672,host2:5672
```

### Query Parameters

| Parameter | Description |
|---|---|
| `exchangetype` | Exchange type: `fanout` (default), `direct`, `topic`, `headers` |
| `exchangename` | Override the exchange name (defaults to the queue name) |
| `route` | Routing key used when binding or publishing |
| `durable` | Queue durability (`true`/`false`/`1`/`0`; default: `true`) |
| `exclusive` | Exclusive queue (`true`/`false`/`1`/`0`; default: `false`) |
| `autodelete` | Auto-delete queue (`true`/`false`/`1`/`0`; default: `false`) |
| `temporary` | Shorthand for `durable=false&exclusive=true&autodelete=true` |
| `ha` | Enable high-availability policy (`x-ha-policy: all`) |
| `ttl` | Message TTL in milliseconds (`x-message-ttl`) |
| `prefetch` | Consumer prefetch count (default: `1`) |
| `consumers` | Number of concurrent consumers (default: `1`) |
| `heartbeat` | Connection heartbeat interval as a .NET `TimeSpan` string (e.g. `60` for 60 s, or `00:01:00`) |
| `hosts` | Comma-separated `host:port` list for cluster connections |
| `tx` | Enable transactional channel (`true`/`false`) |
| `queue.*` | Pass arbitrary queue arguments (e.g. `queue.x-dead-letter-exchange=dlx`) |
| `exchange.*` | Pass arbitrary exchange arguments |

---

## License

This project is licensed under the [MIT License](LICENSE).
