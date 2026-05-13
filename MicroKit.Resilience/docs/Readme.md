# MicroKit.Resilience

Transient error detection, retry with exponential backoff, and circuit breaker protection for .NET 10 services. Built on Polly v8 with pluggable error detectors and optional MediatR pipeline integration.

## Packages

| Package | Purpose |
|---|---|
| `MicroKit.Resilience.Abstractions` | Contracts only — zero third-party dependencies |
| `MicroKit.Resilience` | Core implementation: detectors, pipeline builder, Polly registration |
| `MicroKit.Resilience.MediatR` | MediatR `IPipelineBehavior` that wraps every request in a resilience pipeline |

Each package is independently installable. If you use MediatR for command/query dispatch, add `MicroKit.Resilience.MediatR`. If you want to execute resilience pipelines directly, `MicroKit.Resilience` is sufficient.

## Installation

```bash
dotnet add package MicroKit.Resilience
dotnet add package MicroKit.Resilience.MediatR   # optional
```

## Setup

```csharp
builder.Services
    .AddMicroKitResilience()
    .AddSqlServer()
    .AddHttp()
    .AddPostgres()
    .AddDefaultRetryPolicy(options =>
    {
        options.RetryCount = 3;
        options.BaseDelaySeconds = 1.0;
        options.EnableCircuitBreaker = true;
        options.EnableFallback = true;
    })
    .AddMicroKitResilienceMediatR(); // omit if not using MediatR
```

`AddMicroKitResilience()` returns a `MicroKitResilienceBuilder`. All subsequent calls chain from it. The detector registrations (`AddSqlServer`, `AddHttp`, `AddPostgres`) and the pipeline registration (`AddDefaultRetryPolicy`) are independent — register only what your application contacts.

## Pipeline execution order

Strategies are composed outermost-first:

```
Fallback  ->  Circuit Breaker  ->  Retry  ->  your operation
```

On a transient failure, Retry attempts the operation up to `RetryCount` times with exponential backoff. If all retries fail, Circuit Breaker records the failure against its sampling window. Once `FailureRatio` is exceeded (with at least `MinimumThroughput` calls in the window), the circuit opens and subsequent requests fail fast for `BreakDuration`. Fallback wraps the final exception in `OperationCanceledException` with a stable message, so callers receive a consistent exception type regardless of the underlying infrastructure.

## Configuration reference

All options have production-ready defaults.

| Property | Type | Default | Description |
|---|---|---|---|
| `PipelineName` | `string` | `"DefaultRetry"` | Key used to look up the pipeline in the Polly registry |
| `RetryCount` | `int` | `3` | Maximum retry attempts |
| `BaseDelaySeconds` | `double` | `1.0` | Base delay in seconds; actual delay is exponential with jitter |
| `EnableCircuitBreaker` | `bool` | `true` | Whether to add a circuit breaker to the pipeline |
| `EnableFallback` | `bool` | `true` | Whether to wrap the final failure in `OperationCanceledException` |
| `FailureRatio` | `double` | `0.5` | Failure ratio (0.0–1.0) that opens the circuit |
| `MinimumThroughput` | `int` | `10` | Minimum calls before the circuit breaker evaluates `FailureRatio` |
| `BreakDuration` | `TimeSpan` | `30s` | How long the circuit stays open after tripping |

The circuit breaker sampling window is fixed at 30 seconds. Jitter is always enabled on the retry delay to prevent thundering-herd problems in services with many instances.

Retry attempts are logged at `Warning` level to the `MicroKit.Resilience` logger category with the pipeline name, attempt number, wait duration, and exception message.

## Error detectors

Error detection is delegated to `IResilienceStrategyDetector` implementations. Each detector answers two questions:

- `CanHandle(Exception)` — does this detector recognize this exception type?
- `ShouldRetry(Exception)` — given that it recognizes the exception, is this specific error transient?

`CompositeResilienceDetector` (registered automatically as `IResilienceErrorDetector`) aggregates all registered detectors and returns `true` if any detector both handles and considers the exception transient.

### SQL Server (`AddSqlServer`)

Handles `SqlException` directly and `DbUpdateException` wrapping a `SqlException`. Retries on:

| Error number | Condition |
|---|---|
| 1205 | Deadlock victim |
| 40613 | Database unavailable |
| 40501 | Service currently busy |
| 49918 | Cannot process request — resource limit |
| 49919 | Cannot process request — too many create/update operations |

### PostgreSQL (`AddPostgres`)

Handles `NpgsqlException` directly and `DbUpdateException` wrapping an `NpgsqlException`. Retries on:

| SQLSTATE | Condition |
|---|---|
| 08000 | Connection failure |
| 08003 | Connection does not exist |
| 08006 | Connection failure |
| 53000 | Insufficient resources |
| 53100 | Disk full |
| 53200 | Out of memory |
| 53300 | Too many connections |
| 57014 | Query canceled |

### HTTP (`AddHttp`)

Handles `HttpRequestException`, `TaskCanceledException`, and `SocketException`. Retries on:

- `HttpRequestException` with a 5xx status code, or with no status code (connection could not be established)
- `TaskCanceledException` where `CancellationToken.IsCancellationRequested` is `false` (timeout, not user cancellation)
- Any `SocketException`

`HttpRequestException` with a 4xx status code is not retried — client errors are not transient.

## Custom detectors

Implement `IResilienceStrategyDetector` and register with `AddDetector<T>()`:

```csharp
public sealed class RedisResilienceDetector : IResilienceStrategyDetector
{
    public bool CanHandle(Exception ex)
        => ex is RedisConnectionException or RedisTimeoutException;

    public bool ShouldRetry(Exception ex)
        => ex is RedisConnectionException or RedisTimeoutException;
}

builder.Services
    .AddMicroKitResilience()
    .AddDetector<RedisResilienceDetector>()
    .AddDefaultRetryPolicy();
```

Detectors are registered as `IResilienceStrategyDetector` singletons. `CompositeResilienceDetector` receives all of them via constructor injection.

## MediatR integration

`ResilienceBehavior<TRequest, TResponse>` wraps every `IRequest<TResponse>` handler in a Polly pipeline. It is registered as an open-generic `IPipelineBehavior<,>` via `AddMicroKitResilienceMediatR()`.

By default every request uses the `"DefaultRetry"` pipeline (the value of `ResilienceRetryOptions.PipelineName`). To route a specific request to a different pipeline, implement `IResilientRequest`:

```csharp
public record SyncInventoryCommand(int WarehouseId) : ICommand, IResilientRequest
{
    public string? PipelineName => "HighRetryPolicy";
}
```

When `IResilientRequest.PipelineName` is null or empty, the behavior falls back to the default pipeline name. Additional pipelines are registered by calling `AddResiliencePipeline` from `Microsoft.Extensions.Resilience` directly.

`ResilienceBehavior` calls `pipeline.ExecuteAsync(ct => next(ct), cancellationToken)`, so the cancellation token is forwarded through the retry loop. Polly will cancel the current attempt and stop retrying when the token fires.

## Abstractions contract

`MicroKit.Resilience.Abstractions` has zero third-party dependencies. It contains:

- `IResilienceErrorDetector` — `bool ShouldRetry(Exception)`
- `IResilienceStrategyDetector : IResilienceErrorDetector` — adds `bool CanHandle(Exception)`
- `IResilientRequest` — `string? PipelineName { get; }` marker for per-request pipeline selection

Domain and application code should depend only on `MicroKit.Resilience.Abstractions`. Infrastructure code that needs to register detectors or configure pipelines depends on `MicroKit.Resilience`.

## Constraints and limitations

**Handlers must be idempotent.** Retry will execute the handler body multiple times on transient failures. If your handler writes to a database, sends a message, or calls an external API, ensure those operations are safe to repeat. Use `MicroKit.Idempotency` for deduplication if needed.

**One named pipeline per `AddDefaultRetryPolicy` call.** `AddDefaultRetryPolicy` registers exactly one pipeline under `PipelineName`. For multiple named pipelines with different settings, call `AddResiliencePipeline` from `Microsoft.Extensions.Resilience` directly and route requests to them via `IResilientRequest`.

**Business exceptions are not retried.** The pipeline only retries exceptions for which a registered detector returns `true` from both `CanHandle` and `ShouldRetry`. `InvalidOperationException`, `ValidationException`, `UnauthorizedAccessException`, and similar domain exceptions pass through untouched.

**Circuit breaker state is in-process.** The circuit breaker is a Polly in-memory state machine. It does not share state across service instances. In a multi-instance deployment each instance maintains its own failure counters.

## Dependencies

`MicroKit.Resilience`:
- Polly 8.6.5
- Microsoft.Extensions.Resilience 10.3.0
- Microsoft.Extensions.Options 10.0.3
- Microsoft.Data.SqlClient 6.1.4
- Microsoft.EntityFrameworkCore 10.0.3 (for `DbUpdateException` unwrapping)
- Npgsql 8.0.3

`MicroKit.Resilience.MediatR`:
- MediatR 14.0.0
- Polly 8.6.5
