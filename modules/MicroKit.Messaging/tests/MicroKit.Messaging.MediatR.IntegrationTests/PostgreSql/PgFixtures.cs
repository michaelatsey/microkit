using System.Collections.Concurrent;
using static MicroKit.Result.Result;

namespace MicroKit.Messaging.MediatR.IntegrationTests.PostgreSql;

/// <summary>An effect a handler writes through its store's context: what "applied once" is counted on.</summary>
public sealed class PgEffect
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Value { get; set; } = string.Empty;
}

/// <summary>Store A: the unit of work of the commands, its aggregates and its messaging tables.</summary>
internal sealed class PgOrdersContext(DbContextOptions<PgOrdersContext> options) : PgStoreContext(options);

/// <summary>Store B, for the hosts that configure two outboxes.</summary>
internal sealed class PgBillingContext(DbContextOptions<PgBillingContext> options) : PgStoreContext(options);

internal abstract class PgStoreContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public DbSet<PgEffect> Effects => Set<PgEffect>();

    public DbSet<E2EAggregate> Aggregates => Set<E2EAggregate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyMessagingConfiguration();
        modelBuilder.Entity<PgEffect>().ToTable("Effects").HasKey(e => e.Id);
        modelBuilder.Entity<E2EAggregate>().ToTable("Aggregates").Ignore(nameof(E2EAggregate.DomainEvents));
    }
}

/// <summary>Counts each handler's calls and fails the ones a test arms.</summary>
public sealed class PgProbe
{
    private readonly ConcurrentDictionary<string, int> _calls = new(StringComparer.Ordinal);

    /// <summary>Gets the handlers that throw on their next call, with how many calls fail.</summary>
    public ConcurrentDictionary<string, int> Failures { get; } = new(StringComparer.Ordinal);

    public int Calls(string handler) => _calls.GetValueOrDefault(handler);

    /// <summary>Counts a call; throws when the handler is armed to fail.</summary>
    internal void Enter(string handler)
    {
        _calls.AddOrUpdate(handler, 1, static (_, n) => n + 1);
        if (Failures.TryGetValue(handler, out var left) && left > 0)
        {
            Failures[handler] = left - 1;
            throw new InvalidOperationException($"{handler}: armed failure");
        }
    }
}

// ── V2: one notification, two handlers, one failing ──────────────────────────────────────────

internal sealed record PgOrderSettled(Guid OrderId, DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

internal sealed class PgOrderSettledNotification(PgOrderSettled domainEvent)
    : DomainEventNotification<PgOrderSettled>(domainEvent);

internal sealed class PgLedgerHandler(PgProbe probe, PgOrdersContext store) : INotificationHandler<PgOrderSettledNotification>
{
    public const string Name = "ledger";

    public Task Handle(PgOrderSettledNotification notification, CancellationToken cancellationToken)
    {
        store.Effects.Add(new PgEffect { Value = $"{Name}:{notification.DomainEvent.OrderId}" });
        probe.Enter(Name);
        return Task.CompletedTask;
    }
}

internal sealed class PgReceiptHandler(PgProbe probe, PgOrdersContext store) : INotificationHandler<PgOrderSettledNotification>
{
    public const string Name = "receipt";

    public async Task Handle(PgOrderSettledNotification notification, CancellationToken cancellationToken)
    {
        // Saves first, then may fail: what it saved must roll back with its failure.
        store.Effects.Add(new PgEffect { Value = $"{Name}:{notification.DomainEvent.OrderId}" });
        await store.SaveChangesAsync(cancellationToken);
        probe.Enter(Name);
    }
}

internal sealed record PgSettleOrderCommand(Guid OrderId) : ICommand<Result<Guid>>;

internal sealed class PgSettleOrderHandler(PgOrdersContext store) : ICommandHandler<PgSettleOrderCommand, Result<Guid>>
{
    public ValueTask<Result<Guid>> Handle(PgSettleOrderCommand command, CancellationToken ct = default)
    {
        var order = new E2EAggregate { Id = command.OrderId, Name = "settled" };
        order.Raise(new PgOrderSettled(command.OrderId, DateTimeOffset.UtcNow));
        store.Aggregates.Add(order);   // staged: TransactionBehavior dispatches, then commits
        return new(Success(order.Id));
    }
}

// ── V3: cascades, from a notification handler and from a bus handler ───────────────────────────

internal sealed record PgParcelInspected(Guid ParcelId, DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

internal sealed class PgParcelInspectedNotification(PgParcelInspected domainEvent)
    : DomainEventNotification<PgParcelInspected>(domainEvent);

internal sealed record PgParcelArchived(Guid ParcelId, DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

internal sealed class PgParcelArchivedNotification(PgParcelArchived domainEvent)
    : DomainEventNotification<PgParcelArchived>(domainEvent);

/// <summary>A notification handler raising a cascade event on an aggregate of its store.</summary>
internal sealed class PgArchiveOnInspection(PgProbe probe, PgOrdersContext store) : INotificationHandler<PgParcelInspectedNotification>
{
    public const string Name = "archive-on-inspection";

    public Task Handle(PgParcelInspectedNotification notification, CancellationToken cancellationToken)
    {
        probe.Enter(Name);
        var archived = new E2EAggregate { Name = $"archived:{notification.DomainEvent.ParcelId}" };
        archived.Raise(new PgParcelArchived(notification.DomainEvent.ParcelId, DateTimeOffset.UtcNow));
        store.Aggregates.Add(archived);
        return Task.CompletedTask;
    }
}

internal sealed class PgRecordArchived(PgProbe probe, PgOrdersContext store) : INotificationHandler<PgParcelArchivedNotification>
{
    public const string Name = "record-archived";

    public Task Handle(PgParcelArchivedNotification notification, CancellationToken cancellationToken)
    {
        probe.Enter(Name);
        store.Effects.Add(new PgEffect { Value = $"{Name}:{notification.DomainEvent.ParcelId}" });
        return Task.CompletedTask;
    }
}

internal sealed record PgInspectParcelCommand(Guid ParcelId) : ICommand<Result<Guid>>;

internal sealed class PgInspectParcelHandler(PgOrdersContext store) : ICommandHandler<PgInspectParcelCommand, Result<Guid>>
{
    public ValueTask<Result<Guid>> Handle(PgInspectParcelCommand command, CancellationToken ct = default)
    {
        var parcel = new E2EAggregate { Id = command.ParcelId, Name = "inspected" };
        parcel.Raise(new PgParcelInspected(command.ParcelId, DateTimeOffset.UtcNow));
        store.Aggregates.Add(parcel);
        return new(Success(parcel.Id));
    }
}

/// <summary>A contract a bus handler receives — through the receiving seam, no transport.</summary>
[MessageContract(ContractName)]
public sealed record PgPaymentReceived(Guid ParcelId) : IIntegrationEvent
{
    public const string ContractName = "tests.pg.payment-received";

    public const string WireType = ContractName + ".v1";
}

/// <summary>A bus handler raising a cascade event: delivered to <see cref="PgRecordArchived"/>.</summary>
internal sealed class PgPaymentHandler(PgProbe probe, PgOrdersContext store) : IMessageHandler<PgPaymentReceived>
{
    public const string Name = "payment";

    public ValueTask HandleAsync(PgPaymentReceived message, MessageContext context, CancellationToken ct = default)
    {
        probe.Enter(Name);
        var archived = new E2EAggregate { Name = $"paid:{message.ParcelId}" };
        archived.Raise(new PgParcelArchived(message.ParcelId, DateTimeOffset.UtcNow));
        store.Aggregates.Add(archived);
        return ValueTask.CompletedTask;
    }
}
