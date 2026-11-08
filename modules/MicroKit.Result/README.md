# MicroKit.Result

[![NuGet](https://img.shields.io/nuget/v/MicroKit.Result)](https://www.nuget.org/packages/MicroKit.Result)
[![NuGet](https://img.shields.io/nuget/v/MicroKit.Result.AspNetCore)](https://www.nuget.org/packages/MicroKit.Result.AspNetCore)
[![NuGet](https://img.shields.io/nuget/v/MicroKit.Result.Analyzers)](https://www.nuget.org/packages/MicroKit.Result.Analyzers)

MicroKit.Result makes the outcome of an operation explicit in .NET 10. A method returns a `Result` or a `Result<T>`: a success that holds a value, or a failure that holds a typed error. You compose results with `Map`, `Bind`, `Ensure` and `Match`, map them to HTTP problem details, and keep exceptions for the unexpected.

## Why

The library commits to five guarantees:

- Correctness: an invalid result is never read as a success. A `default` result is neither a success nor a failure, and reading it throws.
- Security: internal details never reach a client by default. The library never turns an exception's text into an error message, a server error (5xx) answers a generic `detail` instead of its message, a result returned as is never writes a system fault's message, and no response writes `Metadata` by default.
- Predictability: synchronous and asynchronous operations behave alike, with the same argument checks and the same rails.
- Interoperability: HTTP concerns stay out of the core. `MicroKit.Result` has no dependency; `MicroKit.Result.AspNetCore` maps error categories to statuses.
- Performance: every performance claim is measured, as below.

Both runtime packages are trim and NativeAOT compatible, checked by the trim and AOT analyzers and by a test project that serializes a `Result<T>` with reflection disabled. The exception is the reflection-based JSON converter factory, annotated `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`; under NativeAOT, register `ResultJsonConverter<T>` next to a source-generated context.

### Performance

Measured with BenchmarkDotNet on one machine, on 2026-11-07. The figures are this machine's, not a promise for yours. Run them with `dotnet run -c Release --project tests/MicroKit.Result.PerformanceTests -- --filter '*'`.

```
BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat) on WSL2
AMD Ryzen 9 7940HS w/ Radeon 780M Graphics 3.99GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.402, .NET 10.0.13, X64 RyuJIT x86-64-v4, DefaultJob
```

Compared libraries: ErrorOr 2.1.1 and FluentResults 4.0.0. Each failure reuses a cached error, so the figures are the result's own cost.

| Creating a result | MicroKit.Result | ErrorOr | FluentResults |
|---|---|---|---|
| Success | below resolution (≈ 0 ns), 0 B | below resolution (≈ 0 ns), 0 B | 15.5 ns, 112 B |
| Failure | below resolution (≈ 0 ns), 0 B | 13.5 ns, 88 B | 18.5 ns, 120 B |

| `Map` → `Bind` → `Map`, lambdas without capture | MicroKit.Result | ErrorOr | FluentResults |
|---|---|---|---|
| Success rail | 5.3 ns, 0 B | 3.1 ns, 0 B | 180.9 ns, 832 B |
| Failure rail | 4.9 ns, 0 B | 3.1 ns, 0 B | 188.3 ns, 896 B |

| Async case, `ValueTask<Result<T>>` against `Task<Result<T>>` | `ValueTask` | `Task` |
|---|---|---|
| `MapAsync`, delegate completes synchronously | 12.6 ns, 72 B | 16.6 ns, 152 B |
| `MapAsync`, delegate truly yields | 583.7 ns, 246 B | 575.8 ns, 231 B |
| A port, completes synchronously | below resolution (≈ 0 ns), 0 B | 8.9 ns, 80 B |
| A port, truly yields | 562.0 ns, 120 B | 528.9 ns, 104 B |

| `ErrorCollection.From` | 3 errors | 10 errors |
|---|---|---|
| As shipped: `Message` built with `string.Join` over LINQ | 67.8 ns, 280 B | 137.0 ns, 616 B |
| Candidate, not shipped: messages joined with `string.Create` into a pooled buffer | 54.7 ns, 232 B | 112.7 ns, 568 B |

On this machine, `Result.Success(value)` and `Result.Failure<T>(error)` do not allocate, and `Map` and `Bind` do not allocate on either rail when their lambdas capture nothing. When an async operation completes synchronously, `ValueTask` is faster and allocates less; when it truly yields, `Task` is up to 6 % faster and allocates 6–13 % less. The `ErrorCollection` candidate gained 18–19 %, under the 20 % required to change the failure-rail path, so the LINQ path stays.

Not measured: a capturing lambda, an `Ensure` whose error is built on every call, a `Where` or a LINQ query, and the effect of `ThrowHelper`'s `[DoesNotReturn]` and `[MethodImpl(NoInlining)]`.

## Getting started

Install the packages. They are published as previews.

```bash
dotnet add package MicroKit.Result --prerelease
dotnet add package MicroKit.Result.AspNetCore --prerelease  # optional: Minimal APIs and MVC
dotnet add package MicroKit.Result.Analyzers --prerelease   # recommended: build-time checks
```

Declare an error, return a result, then consume it:

```csharp
using MicroKit.Result;
using MicroKit.Result.AspNetCore;

// A domain error: one type, which says what kind of failure it reports
public sealed record UserNotFoundError(Guid UserId)
    : Error(ErrorCode.From("USER.NOT_FOUND"), $"User {UserId} not found")
{
    public override ErrorCategory Category => ErrorCategory.NotFound;
}

// A success holds a value; a failure holds an error
public Result<User> GetUser(Guid id) =>
    _users.TryGetValue(id, out User? user)
        ? Result.Success(user)
        : new UserNotFoundError(id);

// Compose on the success rail, then consume both rails
public string Greet(Guid id) =>
    GetUser(id)
        .Map(user => user.Name)
        .Match(
            onSuccess: name => $"Hello, {name}.",
            onFailure: error => error.Message);

// Or answer HTTP: 200 with the user, or problem details with a 404
public void MapEndpoints(WebApplication app) =>
    app.MapGet("/users/{id}", (Guid id) => GetUser(id).ToHttpResult());
```

Read the [usage guide](docs/guides/usage.md) for every task: composing, async, errors, validation, exception boundaries, HTTP, JSON, the analyzers and testing.

## Packages

| Package | What it does |
|---|---|
| `MicroKit.Result` | `Result`, `Result<T>`, errors, validation and the JSON converters. No dependency. |
| `MicroKit.Result.AspNetCore` | `ToHttpResult()` and its siblings, and RFC 9457 problem details. |
| `MicroKit.Result.Analyzers` | Build-time analyzers: `MKR001`, a discarded result; `MKR002`, an error that keeps the default `Technical` category. |

## Getting help

- The [usage guide](docs/guides/usage.md), and its [troubleshooting](docs/guides/usage.md#troubleshooting) section for an exception or a diagnostic.
- The [changelog](CHANGELOG.md), for the breaking changes of each version and their migration notes.
- The repository's [GitHub issues](https://github.com/michaelatsey/microkit/issues), to report a bug or ask a question.

## License

MicroKit.Result is released under the MIT license.
