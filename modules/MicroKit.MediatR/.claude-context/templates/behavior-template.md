# Template: Behavior

Code template for a pipeline behavior + marker. Used by `/new-behavior`.
Replace all `{Placeholder}` values.

---

## File: `I{Marker}.cs` (Abstractions)

```csharp
namespace MicroKit.MediatR.Abstractions;

/// <summary>
/// Opts a request into <see cref="{Name}Behavior{TRequest,TResponse}"/>
/// (pipeline order <see cref="PipelineOrder.{Name}"/>).
/// </summary>
/// <example>
/// <code>
/// public sealed record MyCommand(...) : ICommand&lt;Result&lt;Unit&gt;&gt;, I{Marker}
/// {
///     // configuration required by I{Marker}
/// }
/// </code>
/// </example>
public interface I{Marker}
{
    // Configuration properties (get-only). E.g.:
    // int MaxRetries { get; }
}
```

## File: `PipelineOrder.cs` entry (core)

```csharp
// In PipelineOrder.cs — value must be unique; register it in
// .claude-context/standards/pipeline-order.md.
/// <summary>Order of the {Name} behavior.</summary>
public const int {Name} = {order};   // between adjacent built-ins, or 601–999
```

## File: `{Name}Behavior.cs` (Behaviors)

```csharp
namespace MicroKit.MediatR.Behaviors;

/// <summary>
/// {What the behavior does}. Activated for requests implementing <see cref="I{Marker}"/>.
/// Pipeline order: <see cref="PipelineOrder.{Name}"/> ({order}).
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed class {Name}Behavior<TRequest, TResponse>(
    ILogger<{Name}Behavior<TRequest, TResponse>> logger
    /* + dependencies */)
    : BehaviorBase<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <inheritdoc />
    public override int Order => PipelineOrder.{Name};

    /// <inheritdoc />
    public override async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // 1. Opt-in guard — FIRST statement, zero-cost pass-through
        if (request is not I{Marker} {markerVar})
            return await next().ConfigureAwait(false);

        // 2. Pre-handler logic (ConfigureAwait(false) everywhere)
        // 3. Short-circuit if needed:
        //    - Result<T>: return CreateFailure(error);
        //    - T direct : throw {Exception};

        var response = await next().ConfigureAwait(false);

        // 4. Post-handler logic (if any)
        return response;
    }
}
```

## DI Registration

```csharp
// At the position matching PipelineOrder (MediatR runs behaviors in registration order):
cfg.Add{Name}Behavior();
```

## Benchmark Stub (BenchmarkDotNet)

```csharp
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0)]
[BenchmarkCategory("{Name}Behavior")]
public class {Name}BehaviorBenchmarks
{
    // Budget (from .claude-context/standards/performance-budget.md):
    //   marker absent (pass-through): 0 bytes, ≤ 10 ns

    [GlobalSetup] public void Setup() { /* build behavior + next delegate once */ }

    [Benchmark(Baseline = true)] public Task<object> MarkerAbsent_PassThrough() => /* ... */;
    [Benchmark] public Task<object> MarkerPresent_AppliesLogic() => /* ... */;
}
```

## Rules Applied

- `sealed class` + primary constructor, inherits `BehaviorBase<TRequest, TResponse>`
- `Order` via `PipelineOrder` (unique)
- Guard pattern as the first statement
- `ConfigureAwait(false)` on every await
- No `IMediator` call from inside the behavior
- XML docs on the marker and the behavior; benchmark for the pass-through path
