# Standard: Handler Contracts

**Canonical CQRS contract signatures for MicroKit.MediatR.** These are the exact interfaces in
`MicroKit.MediatR.Abstractions`. Handlers and contracts must match these shapes; `api-reviewer`
enforces them.

---

## Request Contracts

```csharp
/// <summary>A command that mutates state and returns no value.</summary>
public interface ICommand : IRequest;

/// <summary>A command that mutates state and returns <typeparamref name="TResult"/>.</summary>
public interface ICommand<out TResult> : IRequest<TResult>;

/// <summary>A query that reads state and returns <typeparamref name="TResult"/>.</summary>
public interface IQuery<out TResult> : IRequest<TResult>;

/// <summary>A query that streams <typeparamref name="TResult"/> items.</summary>
public interface IStreamQuery<out TResult> : IStreamRequest<TResult>;

/// <summary>A domain fact that has already happened.</summary>
public interface IEvent;

/// <summary>A MediatR notification wrapping a domain event of type <typeparamref name="TEvent"/>.</summary>
public interface IDomainEventNotification<out TEvent> : INotification where TEvent : IEvent
{
    TEvent DomainEvent { get; }
}
```

## Handler Contracts

```csharp
public interface ICommandHandler<in TCommand>
    where TCommand : ICommand
{
    ValueTask Handle(TCommand command, CancellationToken ct = default);
}

public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    ValueTask<TResult> Handle(TCommand command, CancellationToken ct = default);
}

public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    ValueTask<TResult> Handle(TQuery query, CancellationToken ct = default);
}

public interface IStreamQueryHandler<in TQuery, out TResult>
    where TQuery : IStreamQuery<TResult>
{
    IAsyncEnumerable<TResult> Handle(TQuery query, CancellationToken ct = default);
}

public interface IDomainEventHandler<TEvent, in TNotification>
    where TEvent : IEvent
    where TNotification : IDomainEventNotification<TEvent>
{
    // MediatR INotificationHandler contract — returns Task, not ValueTask.
    Task Handle(TNotification notification, CancellationToken cancellationToken);
}
```

## Marker Contracts

```csharp
/// <summary>Opts a command into IdempotencyBehavior (order 400).</summary>
public interface IIdempotentCommand
{
    string IdempotencyKey { get; }   // non-null, non-empty
}

/// <summary>Opts a query into CachingBehavior (order 500).</summary>
public interface ICacheableQuery
{
    string CacheKey { get; }         // non-null, non-empty
    TimeSpan? Expiry { get; }        // null = no expiry (audit WARNING)
}

/// <summary>Opts a request into RetryBehavior (order 600).</summary>
public interface IRetryableRequest
{
    int MaxRetries { get; }          // > 0
    TimeSpan Delay { get; }
}

/// <summary>Opts a request into AuthorizationBehavior (order 200).</summary>
public interface IAuthorizedRequest
{
    string[] RequiredPolicies { get; } // non-empty
}
```

## Signature Rules

- Command/Query handlers return **`ValueTask`/`ValueTask<T>`** — never `Task<T>`.
- Notification handlers return **`Task`** (MediatR `INotificationHandler` contract).
- Stream handlers return **`IAsyncEnumerable<T>`**; the implementation uses `[EnumeratorCancellation]`.
- `CancellationToken ct = default` is always the last parameter (Command/Query). Notification
  handlers keep MediatR's `CancellationToken cancellationToken` name without a default.
- `TResult` may be `Result<T>`, `T`, or `Unit` — never `Result<Result<T>>`.
