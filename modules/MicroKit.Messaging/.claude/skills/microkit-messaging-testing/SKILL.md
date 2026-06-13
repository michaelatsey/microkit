# Skill: microkit-messaging-testing

How to run, filter, and interpret tests for MicroKit.Messaging.

## Run All Tests

```bash
dotnet test modules/MicroKit.Messaging/MicroKit.Messaging.slnx
```

## Run by Category

```bash
# Unit tests only
dotnet test modules/MicroKit.Messaging/tests/MicroKit.Messaging.UnitTests/

# Integration tests (requires SQLite or Testcontainers)
dotnet test modules/MicroKit.Messaging/tests/MicroKit.Messaging.IntegrationTests/

# Architecture tests
dotnet test modules/MicroKit.Messaging/tests/MicroKit.Messaging.ArchitectureTests/

# Performance tests (BenchmarkDotNet — Release config required)
dotnet run --project modules/MicroKit.Messaging/tests/MicroKit.Messaging.PerformanceTests/ -c Release
```

## Filter Tests by Name

```bash
# All outbox-related tests
dotnet test modules/MicroKit.Messaging/MicroKit.Messaging.slnx \
  --filter "FullyQualifiedName~Outbox"

# Dedup tests only
dotnet test modules/MicroKit.Messaging/MicroKit.Messaging.slnx \
  --filter "FullyQualifiedName~ExistsAsync"

# Failed/retry tests
dotnet test modules/MicroKit.Messaging/MicroKit.Messaging.slnx \
  --filter "FullyQualifiedName~Failed OR FullyQualifiedName~Retry OR FullyQualifiedName~DeadLetter"
```

## Background Worker Testing Patterns

### Testing OutboxProcessor in isolation

```csharp
[Fact]
public async Task ProcessBatch_WhenMessagesPending_PublishesAndMarksPublished()
{
    // Arrange
    var store = new InMemoryOutboxStore();
    var publisher = new FakeMessagePublisher();
    var logger = NSubstitute.Substitute.For<ILogger<OutboxProcessor>>();
    // OutboxMessage is sealed class with { get; set; } — not a record
    var message = new OutboxMessage
    {
        Id = new MessageId(Guid.NewGuid()),
        TenantId = "tenant-abc",
        EventType = typeof(OrderPlacedEvent).FullName!,
        Payload = """{"OrderId":"..."}""",
        Status = OutboxMessageStatus.Pending,
        OccurredOnUtc = DateTimeOffset.UtcNow,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        CorrelationId = new CorrelationId(Guid.NewGuid()),
    };
    await store.AddAsync(message);

    var processor = new OutboxProcessor(
        CreateScopeFactory(store, publisher), logger,
        new OutboxProcessorOptions { BatchSize = 10 });

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

    // Act
    await processor.ProcessBatchAsync(cts.Token);

    // Assert
    publisher.ShouldHavePublished<OrderPlacedEvent>();
    var pending = await store.GetPendingAsync(10, tenantId: "tenant-abc", cts.Token);
    pending.ShouldBeEmpty();
}
```

### Testing idempotency gate (InboxProcessor)

```csharp
[Fact]
public async Task ProcessMessage_WhenAlreadyProcessed_SkipsHandler()
{
    // Arrange
    var inboxStore = new InMemoryInboxStore();
    var handler = NSubstitute.Substitute.For<IMessageHandler<OrderPlacedEvent>>();
    var messageId = new MessageId(Guid.NewGuid());
    // consumerType comes from the handler type — not from the inbox row
    var consumerType = handler.GetType().FullName!;

    // Pre-seed as already processed
    await inboxStore.AddAsync(new InboxMessage
    {
        MessageId = messageId,
        ConsumerType = consumerType,
        TenantId = "tenant-abc",
        Status = InboxMessageStatus.Processed,
        ReceivedAtUtc = DateTimeOffset.UtcNow,
        EventType = typeof(OrderPlacedEvent).FullName!,
        Payload = """{}""",
        CorrelationId = new CorrelationId(Guid.NewGuid()),
    });

    var processor = new InboxProcessor(
        CreateScopeFactory(inboxStore, handler), logger,
        new InboxProcessorOptions { BatchSize = 10 });

    var envelope = new MessageEnvelope<OrderPlacedEvent>
    {
        MessageId = messageId,
        TenantId = "tenant-abc",
        Event = new OrderPlacedEvent(...),
        CorrelationId = new CorrelationId(Guid.NewGuid()),
    };

    // Act — processor sees the existing row; handler must NOT be invoked
    await processor.ProcessAsync(envelope, CancellationToken.None);

    // Assert — handler was never called because idempotency gate fired
    await handler.DidNotReceive().HandleAsync(Arg.Any<OrderPlacedEvent>(), Arg.Any<CancellationToken>());
}
```

### Tenant-scoped test helper pattern

```csharp
// ✅ Tenant-scoped InMemoryOutboxStore query
// GetPendingAsync always requires a tenantId — cross-tenant queries are forbidden
[Fact]
public async Task GetPendingAsync_OnlyReturnsTenantMessages()
{
    var store = new InMemoryOutboxStore();

    await store.AddAsync(CreateOutboxMessage(tenantId: "tenant-a"));
    await store.AddAsync(CreateOutboxMessage(tenantId: "tenant-b"));

    var tenantAMessages = await store.GetPendingAsync(
        batchSize: 10, tenantId: "tenant-a", CancellationToken.None);
    tenantAMessages.Count.ShouldBe(1);
    tenantAMessages[0].TenantId.ShouldBe("tenant-a");
}
```

## Architecture Test Quick-Run

```bash
dotnet test modules/MicroKit.Messaging/tests/MicroKit.Messaging.ArchitectureTests/ -v normal
```

Expected output: all tests pass. Any failure indicates a layer boundary violation.

## Detecting Banned Libraries

```bash
# FluentAssertions check
grep -rn "FluentAssertions\|\.Should()\." \
  modules/MicroKit.Messaging/tests/ --include="*.cs" | head -20

# MediatR.Contracts check
grep -rn "MediatR\.Contracts\|INotification" \
  modules/MicroKit.Messaging/ --include="*.cs" --include="*.csproj" | head -20
```

Both commands must return no output for a clean module.

## SQLite Integration Test Isolation

Each integration test must use an isolated SQLite in-memory database:

```csharp
// ✅ Per-test isolation — unique database name prevents cross-test contamination
public class OutboxIntegrationTests : IAsyncLifetime
{
    private readonly string _dbName = $"test-{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        // Each test gets its own SQLite in-memory database
        var options = new DbContextOptionsBuilder<MessagingDbContext>()
            .UseSqlite($"Data Source={_dbName};Mode=Memory;Cache=Shared")
            .Options;
        // ...
    }
}

// ❌ Shared database across tests — state bleeds between tests
private static readonly string _dbName = "shared-test-db"; // ← static = shared state
```
