using FluentValidation;
using MediatR;
using MicroKit.MediatR;
using MicroKit.Result;
using static MicroKit.Result.Result;

namespace MicroKit.MediatR.IntegrationTests.Fixtures;

// ── Simple echo command (no external deps) ─────────────────────────────────

internal sealed record EchoCommand(string Message) : ICommand<Result<string>>;

internal sealed class EchoHandler : ICommandHandler<EchoCommand, Result<string>>
{
    public ValueTask<Result<string>> Handle(EchoCommand command, CancellationToken ct = default)
        => new(Success(command.Message));
}

// ── Validatable command ────────────────────────────────────────────────────

internal sealed record ValidatableCommand(int Value) : ICommand<Result<int>>;

internal sealed class ValidatableCommandValidator : AbstractValidator<ValidatableCommand>
{
    public ValidatableCommandValidator()
        => RuleFor(x => x.Value).GreaterThan(0).WithMessage("Value must be positive");
}

internal sealed class ValidatableHandler : ICommandHandler<ValidatableCommand, Result<int>>
{
    public ValueTask<Result<int>> Handle(ValidatableCommand command, CancellationToken ct = default)
        => new(Success(command.Value * 2));
}

// ── Authorized command ─────────────────────────────────────────────────────

internal sealed record SecureCommand : ICommand<Result<string>>, IAuthorizedRequest
{
    public string[] RequiredPolicies => ["Admin"];
}

internal sealed class SecureHandler : ICommandHandler<SecureCommand, Result<string>>
{
    public ValueTask<Result<string>> Handle(SecureCommand command, CancellationToken ct = default)
        => new(Success("authorized"));
}

// ── Simple query ───────────────────────────────────────────────────────────

internal sealed record DoubleQuery(int Input) : IQuery<Result<int>>;

internal sealed class DoubleHandler : IQueryHandler<DoubleQuery, Result<int>>
{
    public ValueTask<Result<int>> Handle(DoubleQuery query, CancellationToken ct = default)
        => new(Success(query.Input * 2));
}

// ── Cancellation-aware command ─────────────────────────────────────────────

internal sealed record CancellableCommand : ICommand<Result<string>>;

internal sealed class CancellableHandler : ICommandHandler<CancellableCommand, Result<string>>
{
    public ValueTask<Result<string>> Handle(CancellableCommand command, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return new(Success("done"));
    }
}

// ── Domain events: single handler (ItemCreated) ───────────────────────────

internal sealed record ItemCreatedEvent(Guid ItemId) : IEvent;

internal sealed class ItemCreatedNotification(ItemCreatedEvent domainEvent)
    : DomainEventNotification<ItemCreatedEvent>(domainEvent);

internal sealed class RecordItemCreatedHandler(DomainEventLog log)
    : IDomainEventHandler<ItemCreatedEvent>
{
    public Task Handle(ItemCreatedEvent domainEvent, CancellationToken cancellationToken)
    {
        log.ItemCreatedIds.Add(domainEvent.ItemId);
        return Task.CompletedTask;
    }
}

// ── Domain events: two handlers = fan-out (OrderPlaced) ───────────────────

internal sealed record OrderPlacedEvent(Guid OrderId) : IEvent;

internal sealed class OrderPlacedNotification(OrderPlacedEvent domainEvent)
    : DomainEventNotification<OrderPlacedEvent>(domainEvent);

internal sealed class OrderPlacedHandlerOne(DomainEventLog log)
    : IDomainEventHandler<OrderPlacedEvent>
{
    public Task Handle(OrderPlacedEvent domainEvent, CancellationToken cancellationToken)
    {
        log.OrderPlacedInvocations++;
        return Task.CompletedTask;
    }
}

internal sealed class OrderPlacedHandlerTwo(DomainEventLog log)
    : IDomainEventHandler<OrderPlacedEvent>
{
    public Task Handle(OrderPlacedEvent domainEvent, CancellationToken cancellationToken)
    {
        log.OrderPlacedInvocations++;
        return Task.CompletedTask;
    }
}

// ── Retryable command fixtures ─────────────────────────────────────────────
// Each retryable command uses a DISTINCT request type so the RetryBehavior's
// process-wide Polly pipeline cache (keyed by TRequest) does not bleed between tests.

internal sealed class AttemptCounter { public int Count; }

internal sealed record RetrySucceedAfterTwoCommand : ICommand<Result<string>>, IRetryableRequest
{
    public int MaxRetries => 3;
    public TimeSpan Delay => TimeSpan.FromMilliseconds(1);
}

internal sealed class RetrySucceedAfterTwoHandler(AttemptCounter counter)
    : ICommandHandler<RetrySucceedAfterTwoCommand, Result<string>>
{
    public ValueTask<Result<string>> Handle(RetrySucceedAfterTwoCommand command, CancellationToken ct = default)
    {
        counter.Count++;
        if (counter.Count < 3)
            throw new IOException("transient failure");
        return new(Success("after retries"));
    }
}

internal sealed record RetryAlwaysFailCommand : ICommand<Result<string>>, IRetryableRequest
{
    public int MaxRetries => 2;
    public TimeSpan Delay => TimeSpan.FromMilliseconds(1);
}

internal sealed class RetryAlwaysFailHandler(AttemptCounter counter)
    : ICommandHandler<RetryAlwaysFailCommand, Result<string>>
{
    public ValueTask<Result<string>> Handle(RetryAlwaysFailCommand command, CancellationToken ct = default)
    {
        counter.Count++;
        throw new IOException("always fails");
    }
}

// ── Idempotent command fixtures ────────────────────────────────────────────

internal sealed record IdempotentEchoCommand(string Key, string Message) : ICommand<Result<string>>, IIdempotentCommand
{
    public string IdempotencyKey => Key;
}

internal sealed class IdempotentEchoHandler(AttemptCounter counter)
    : ICommandHandler<IdempotentEchoCommand, Result<string>>
{
    public ValueTask<Result<string>> Handle(IdempotentEchoCommand command, CancellationToken ct = default)
    {
        counter.Count++;
        return new(Success(command.Message));
    }
}

// ── Cacheable query fixtures ───────────────────────────────────────────────

internal sealed record CacheableDoubleQuery(string Key, int Input) : IQuery<Result<int>>, ICacheableQuery
{
    public string CacheKey => Key;
    public TimeSpan? Expiry => TimeSpan.FromMinutes(5);
}

internal sealed class CacheableDoubleHandler(AttemptCounter counter)
    : IQueryHandler<CacheableDoubleQuery, Result<int>>
{
    public ValueTask<Result<int>> Handle(CacheableDoubleQuery query, CancellationToken ct = default)
    {
        counter.Count++;
        return new(Success(query.Input * 2));
    }
}

// ── Domain event with no handler (for dispatch-time error test) ───────────

internal sealed record UnregisteredEvent(Guid Id) : IEvent;

// ── Shared domain event log (singleton in DI) ─────────────────────────────

internal sealed class DomainEventLog
{
    public List<Guid> ItemCreatedIds { get; } = [];
    public int OrderPlacedInvocations { get; set; }
}
