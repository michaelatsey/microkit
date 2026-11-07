# MicroKit.Result

Railway-Oriented Programming for .NET 10. Explicit success/failure without exceptions.

[![NuGet](https://img.shields.io/nuget/v/MicroKit.Result)](https://www.nuget.org/packages/MicroKit.Result)
[![NuGet](https://img.shields.io/nuget/v/MicroKit.Result.AspNetCore)](https://www.nuget.org/packages/MicroKit.Result.AspNetCore)

---

## Overview

`MicroKit.Result` replaces exception-as-control-flow with an expressive, composable `Result<T>` type. It is a zero-dependency library built on two rails:

```
──── Success ──────────────────────────────────────────────────►
──── Failure ──────────────────────────────────────────────────►
```

Every operation stays on one of these rails: a predictable outcome is a value, not a stack unwind. Exceptions remain for misuse, such as reading `.Value` on a failure (see [Exception Handling](#exception-handling)).

**Key design principles:**
- `Result<T>` is a `readonly struct`; whether a pipeline allocates depends on its lambdas and errors (see [Performance Notes](#performance-notes))
- `byte _tag` discriminator: `default` is neither success nor failure, and reading it throws
- `sealed` by default; throws live in a `ThrowHelper`, outside the calling methods
- Trim- and NativeAOT-compatible, checked by the trim, single-file and AOT analyzers (`IsAotCompatible`, no warning); reflection is confined to the JSON converters (`MicroKit.Result.Serialization` namespace, annotated `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`)

---

## Installation

```bash
dotnet add package MicroKit.Result --prerelease
dotnet add package MicroKit.Result.AspNetCore --prerelease  # optional — Minimal APIs / MVC
dotnet add package MicroKit.Result.Analyzers --prerelease   # recommended — MKR001 discarded result, MKR002 default category
```

The published previews predate this documentation; releases are governed by ADR-GLOBAL-002.

---

## Quick Start

```csharp
using MicroKit.Result;
using MicroKit.Result.AspNetCore;
using Microsoft.AspNetCore.Http;

// Create
Result<User> result = GetUser(id);

// Transform
Result<UserDto> dto = result.Map(u => mapper.ToDto(u));

// Chain
Result<Invoice> invoice = GetUser(id)
    .Bind(user => GetCart(user.CartId))
    .Bind(cart => CreateInvoice(cart));

// Consume
IResult response = invoice.ToHttpResult(); // 200 OK or ProblemDetails
```

---

## Core Types

| Type | Description |
|------|-------------|
| `Result` | Outcome without a value (commands, side-effects) |
| `Result<T>` | Outcome with a value (queries, transformations) |
| `Unit` | Void-safe type parameter — use where `void` is not permitted |

```csharp
Result success = Result.Success();
Result failure = Result.Failure(new MyError());

Result<int> value = Result.Success(42);
Result<int> failed = Result.Failure<int>(new MyError());

// implicit conversion from T
Result<int> fromValue = 42;

// implicit conversion from an error: a failure
Result<int> fromError = new MyError();
Result commandFailed = new MyError();
```

`Success` and the conversion from `T` reject `null` with an `ArgumentNullException`: absence is a failure (`Error.NotFound`), not a null value. They reject a value that is itself a `Result` or a `Result<T>` (possible when `T` is `object`) with an `ArgumentException`: a failure never becomes a success. The conversion from an `Error` or an `ErrorCollection` gives a failure, except into `Result<SomeErrorType>`, where the conversion from `T` wins, and into `Result<Error>`, where it is ambiguous (CS0457): write `Success` or `Failure` there.

---

## Error System

### IError / Error

```csharp
// Define domain-specific typed errors
public sealed record UserNotFoundError(Guid UserId)
    : Error(ErrorCode.From("USER.NOT_FOUND"), $"User {UserId} not found")
{
    public override ErrorCategory Category => ErrorCategory.NotFound;
}
```

### Error factories

A domain error is one type each, as above. At a boundary, or for an error used once, a factory of `Error` builds a `CategorizedError` with an explicit category; there is one per `ErrorCategory`:

```csharp
// Ad-hoc error with an explicit category: one factory per ErrorCategory
CategorizedError missing = Error.NotFound(ErrorCode.From("ORDER.NOT_FOUND"), $"Order {orderId} does not exist.");
return missing; // implicit conversion: a failure
```

### ErrorCode

Strongly-typed hierarchical code: `DOMAIN.ENTITY.REASON`. The code is the error's stable identifier; the predefined constants are convenience codes for an error without a domain code, not a second taxonomy: `Category`, not `Code`, drives behavior.

```csharp
var code = ErrorCode.From("ORDER.PAYMENT.DECLINED");
string raw = code;   // implicit string conversion

// Predefined constants
ErrorCode notFound = ErrorCode.NotFound;                // "NOT_FOUND"
ErrorCode validation = ErrorCode.Validation;              // "VALIDATION"
ErrorCode timeout = ErrorCode.Timeout;                 // "TIMEOUT"
ErrorCode external = ErrorCode.ExternalServiceFailure;  // "EXTERNAL_SERVICE_FAILURE"
// ...19 predefined constants total

// Comparable
bool ordered = ErrorCode.Conflict < ErrorCode.NotFound;   // ordinal comparison
```

### ErrorCategory → HTTP mapping

`Category` says what kind of failure happened. `Severity` serves logging only, and `Metadata` is in-process context that no response emits by default (ADR-RESULT-001 D1). `MicroKit.Result.AspNetCore` maps the category to a status (`ResultProblemDetailsFactory.ToStatusCode`):

| Category | HTTP | Description |
|----------|------|-------------|
| `NotFound` | 404 | Resource does not exist |
| `Validation` | 422 | Input validation failed |
| `BusinessRule` | 422 | Domain business rule violated |
| `Unauthorized` | 401 | Authentication required |
| `Forbidden` | 403 | Access denied |
| `Conflict` | 409 | Resource conflict |
| `TooManyRequests` | 429 | Rate limit exceeded |
| `Timeout` | 504 | Operation, typically a call to a dependency, did not complete in time |
| `Cancelled` | 499 | Operation cancelled, other than by the caller's own cancellation, which propagates instead |
| `PreconditionFailed` | 412 | Precondition not met |
| `External` | 502 | External dependency failed |
| `NotSupported` | 501 | Operation not supported |
| `Unavailable` | 503 | Service temporarily unavailable |
| `Technical` | 500 | Internal error (default) |

A domain error that does not override `Category` is `Technical`, so a 500: the analyzer `MKR002` of `MicroKit.Result.Analyzers` reports it.

### ErrorMetadataBuilder

```csharp
IReadOnlyDictionary<string, object?> meta = new ErrorMetadataBuilder()
    .WithTimestamp()
    .WithCorrelationId(httpContext.TraceIdentifier)
    .WithTraceId()           // Activity.Current?.TraceId
    .Add("userId", userId)
    .Build();
```

### ErrorCollection

Aggregates multiple errors into one `IError`; it is never empty. Its `Category` is the one its errors share; otherwise the first fault of the system (`Technical`, `Unavailable`, `External`, `NotSupported`, `Timeout`), so a server fault is never hidden behind a client error; otherwise the first error's.

```csharp
var collection = ErrorCollection.From(error1, error2, error3);

// Filter: a filter may match nothing, so it returns a list, never an empty collection
IReadOnlyList<IError> validation = collection.WithCategory(ErrorCategory.Validation);
IReadOnlyList<IError> critical = collection.WithSeverity(ErrorSeverity.Critical);
IReadOnlyList<IError> notFound = collection.WithCode(ErrorCode.NotFound);
IReadOnlyList<ValidationError> typed = collection.OfType<ValidationError>();
bool hasValidation = collection.HasCategory(ErrorCategory.Validation);

// Group
IReadOnlyDictionary<ErrorCategory, IReadOnlyList<IError>> byCategory =
    collection.GroupByCategory();

// Flatten nested collections
ErrorCollection flat = collection.Flatten();
```

### IError extension methods

```csharp
bool isValidation = error.IsCategory(ErrorCategory.Validation);
bool isCritical = error.IsCritical();
bool isWarning = error.IsWarning();
bool isInformation = error.IsInformation();
bool hasCorrelationId = error.HasMetadata("correlationId");
string? correlationId = error.TryGetMetadata<string>("correlationId");
ResultException thrown = error.ToException();   // wraps in ResultException
```

---

## Pipelines

### Map — transform the value

```csharp
Result<string> name = GetUser(id).Map(u => u.Name);
```

### Bind — chain result-producing operations

```csharp
Result<Invoice> invoice = GetUser(id)
    .Bind(u => GetCart(u.CartId))
    .Bind(c => CreateInvoice(c));
```

### Match — consume both rails

```csharp
string label = result.Match(
    onSuccess: v => $"Got {v}",
    onFailure: e => $"Error: {e.Message}");
```

### Tap / TapError — side-effects without leaving the rail

Logging goes through source-generated `[LoggerMessage]` methods:

```csharp
using Microsoft.Extensions.Logging;

internal static partial class PipelineLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded value {Value}")]
    public static partial void ValueLoaded(this ILogger logger, int value);

    [LoggerMessage(Level = LogLevel.Information, Message = "Completed, success: {IsSuccess}")]
    public static partial void Completed(this ILogger logger, bool isSuccess);
}
```

```csharp
Result<int> observed = result
    .Tap(v => logger.ValueLoaded(v))
    .TapError(e => metrics.RecordFailure(e.Code));
```

### Ensure — guard on success

```csharp
Result<User> active = GetUser(id)
    .Ensure(u => u.IsActive, new UserInactiveError(id))
    .Ensure(u => u.IsEmailVerified, new EmailNotVerifiedError());
```

`Ensure` takes the error already built, so it is built on every call, success included, interpolated message and all. When that costs, `Bind` builds it only on failure:

```csharp
Result<User> activeLazy = GetUser(id)
    .Bind(u => u.IsActive ? Result.Success(u) : Result.Failure<User>(new UserInactiveError(id)));
```

### MapError — transform the error

```csharp
Result<Order> result = GetOrder(id)
    .MapError(e => new EnrichedError(e, correlationId));
```

`EnrichedError` is an application type. A wrapper like it forwards the `Category`, `Severity` and `Metadata` of the error it wraps, or the response falls back to 500; `EnrichedError` adds its `CorrelationId` to that `Metadata` (see `samples/MicroKit.Result.Samples/Readme/Pipelines.cs`). Wrapping an `ErrorCollection` still drops its `errors` list from the `ProblemDetails`.

### Compensate — fallback on failure

```csharp
Result<Product> product = GetProduct(id)
    .Compensate(_ => GetDefaultProduct());
```

### ValueAccess — read the value, with a fallback or a throw

```csharp
int value = result.GetValueOrDefault(0);

int strict = result.GetValueOrThrow();                          // ResultException on failure
int custom = result.GetValueOrThrow(e => new MyException(e));  // custom exception

if (result.TryGetValue(out var v))
    Process(v);
```

### Finally — act on the result, whatever its rail

```csharp
Result<int> completed = result.Finally(r => logger.Completed(r.IsSuccess));
```

`Finally` runs only when a result reaches it. An exception thrown earlier in the chain, a `default` result that an earlier step reads, or a faulted task skips it: it does not replace `try` / `finally` for cleanup.

### Conversion

```csharp
Result nonGeneric = result.ToResult();  // drops the value, keeps the error
```

---

## Async

The nine pipeline operations (`Map`, `Bind`, `Match`, `Tap`, `TapError`, `Ensure`, `MapError`, `Compensate`, `Finally`) have async counterparts on `Result<T>` and on the non-generic `Result`, and with a synchronous delegate on `Task<Result<T>>` and `ValueTask<Result<T>>`. Each `XxxAsync` has two forms: a delegate that returns a `Task`, without a token; and a delegate that takes the `CancellationToken` and returns a `ValueTask`, with `CancellationToken ct = default` last, passed to the delegate — the shape of a port such as `ValueTask<Result<T>> GetAsync(Guid id, CancellationToken ct = default)`.

```csharp
// Surface 1: async delegate on Result<T>
Result<string> mapped = await result.MapAsync(async v => await TransformAsync(v));
Result<string> chained = await result.BindAsync(async v => await GetNextAsync(v));
Result<int> recovered = await result.CompensateAsync(async e => await FallbackAsync(e));
Result<int> finished = await result.FinallyAsync(async r => await LogAsync(r));

// Surface 2: on Task<Result<T>> (sync delegate — awaits the task first)
Result<string> mappedTask = await GetResultTask().Map(v => Transform(v));
Result<string> chainedTask = await GetResultTask().Bind(v => GetNext(v));

// Tap, TapError and Finally given an async lambda await the side effect, on every surface
Result<int> audited = await GetResultTask().Tap(async v => await AuditAsync(v));

// Surface 3: on ValueTask<Result<T>> (sync delegate)
Result<string> mappedValueTask = await GetResultValueTask().Map(v => Transform(v));

// CancellationToken form: the delegate takes the token and returns a ValueTask, as a port does
Result<string> fetched = await result.BindAsync((v, token) => FetchAsync(v, token), ct);

// The non-generic Result has the same operations, in both forms
Result saved = await command.BindAsync(async () => await SaveAsync());
Result audit = await command.TapAsync(token => AuditCommandAsync(token), ct);
```

All async methods use `ConfigureAwait(false)`, and validate their arguments when called, before anything is awaited: a `null` delegate throws at the call, as in the synchronous methods.

`Tap`, `TapError` and `Finally` given a delegate that returns a `Task`, an async lambda included, bind to an overload that awaits the side effect and returns a `ValueTask`, on `Result<T>`, `Result` and both task surfaces. An `Action` keeps the synchronous overload. A non-async lambda that returns a `ValueTask` binds to that `Action` overload and its `ValueTask` is discarded, which CA2012 reports: write it `async x => await port(x)`.

The extensions pass the token to the delegate and neither check it nor catch a cancellation: it propagates. A `Task` and a `ValueTask` overload without a token would make every async lambda ambiguous (CS0121), so `ValueTask` comes with the token form; an async lambda with one parameter, `async v => …`, takes the `Task` form. On `Task<Result<T>>` and `ValueTask<Result<T>>`, await first to use an async delegate.

---

## Validation

### ValidationError factories

```csharp
ValidationError[] errors =
[
    ValidationError.Required("Email"),
    ValidationError.MinLength("Password", 8),
    ValidationError.MaxLength("Username", 50),
    ValidationError.StringLength("Bio", 10, 500),
    ValidationError.OutOfRange("Age", min: 0, max: 120),
    ValidationError.InvalidFormat("PostalCode", hint: "5 digits"),
    ValidationError.InvalidEmail(),
    ValidationError.InvalidUrl("WebsiteUrl"),
    ValidationError.Custom("Field", "Custom message"),
];
```

### ValidationResult — collect all errors

```csharp
var validation = new ValidationResult()
    .AddErrorIf(string.IsNullOrEmpty(name), "Name", "Name is required.")
    .AddErrorIf(age < 0, "Age", "Age must be non-negative.")
    .AddErrorIf(!email.Contains('@'), "Email", "Email is not a valid email address.");

if (!validation.IsValid)
    return validation.ToResult<Order>(); // a failure holding every validation error

return CreateOrder(name, age, email);
```

`ToResult<T>()` is failure-only: on a valid `ValidationResult` it throws `InvalidOperationException`. `ToResult<T>(value)` gives the success or the failure.

---

## Combining Results

```csharp
// Fail-fast — returns the first failure
Result combined = Result.Combine(validateName, validateEmail, validateAge);

// Collect-all — returns ErrorCollection with ALL errors
Result allErrors = Result.CombineAll(validateName, validateEmail, validateAge);

// Tuple combination (up to 3)
Result<(User, Cart)> pair = Result.Combine(getUser, getCart);
```

Every argument is evaluated before the call: to skip a step after a failure, chain it with `Bind`.

Every overload throws `ResultException` on a `default` result.

---

## LINQ Query Syntax

```csharp
var result =
    from user in GetUser(id)
    from cart in GetCart(user.CartId)
    from order in CreateOrder(cart)
    select order.ToDto();

// Where (requires an error on false)
Result<int> positive = Result.Success(42)
    .Where(v => v > 0, new NegativeValueError());

// AsEnumerable — 0 or 1 element
foreach (var value in result.AsEnumerable())
    Process(value);
```

---

## Enumerable Extensions

```csharp
IEnumerable<Result<T>> results = GetAllResults();

// Split into successes and failures
var (successes, failures) = results.Partition();

// Extract only successes / only failures
IEnumerable<T> values = results.Successes();
IEnumerable<IError> errors = results.Failures();

// Map each element, fail-fast on first error
Result<IReadOnlyList<Dto>> dtos = users.Traverse(u => MapToDto(u));
```

`Partition`, `Successes` and `Failures` throw `ResultException` on a `default` result.

---

## ASP.NET Core

```csharp
using MicroKit.Result;
using MicroKit.Result.AspNetCore;
using Microsoft.AspNetCore.Builder;

// Minimal API; ct is the request's token, cancelled when the client goes away
app.MapGet("/users/{id}", async (Guid id, IUserService svc, CancellationToken ct) =>
    (await svc.GetUserAsync(id, ct)).ToHttpResult());

// Result without value (command)
app.MapDelete("/users/{id}", async (Guid id, IUserService svc, CancellationToken ct) =>
    (await svc.DeleteUserAsync(id, ct)).ToHttpResult());

// 201 Created with the location of the new resource; problem responses declared for OpenAPI
app.MapPost("/users", async (CreateUser command, IUserService svc, CancellationToken ct) =>
        (await svc.CreateUserAsync(command, ct)).ToCreatedHttpResult(user => $"/users/{user.Id}"))
    .ProducesProblem(StatusCodes.Status409Conflict)
    .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);
```

`ToHttpResult()`, `ToCreatedHttpResult(location)` and `ToAcceptedHttpResult(location)` return `TypedResults` (`Results<Ok<T>, ProblemHttpResult>`…): OpenAPI sees the success type. A `ProblemHttpResult` has no fixed status, so an endpoint declares its problem responses with `ProducesProblem` or `ProducesValidationProblem`.

On failure the body is an RFC 9457 `ProblemDetails` with the status of the error's `ErrorCategory`. It is written through the host's `IProblemDetailsService` when `AddProblemDetails()` registers one, so `CustomizeProblemDetails` and the `traceId` apply. `type` is the section ASP.NET Core knows for the status (RFC 9110; RFC 4918 for 422), absent for 429 and 499:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Not Found",
  "status": 404,
  "detail": "User abc123 not found",
  "errorCode": "USER.NOT_FOUND"
}
```

`detail` is `IError.Message`, which an error keeps safe for a client; `Metadata` is never written. For an `ErrorCollection`, `errorCodes` lists the code of each error, nested collections flattened. A validation error, alone or in an `ErrorCollection`, answers an `HttpValidationProblemDetails` whose `errors` groups its messages by `PropertyName`, so a front end can attach them to form fields; a 5xx response never carries them:

```json
{
  "type": "https://tools.ietf.org/html/rfc4918#section-11.2",
  "title": "Validation Failed",
  "status": 422,
  "detail": "Email is required.; Email is not a valid email address.",
  "errors": {
    "Email": ["Email is required.", "Email is not a valid email address."]
  },
  "errorCode": "MULTI.ERROR",
  "errorCodes": ["VALIDATION.EMAIL", "VALIDATION.EMAIL"]
}
```


---

## Serialization

```csharp
using MicroKit.Result.Serialization;

// Register for Minimal APIs
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new ResultJsonConverterFactory()));

// Register for MVC controllers, which do not read the Minimal API options
builder.Services.AddControllers().AddJsonOptions(o =>
    o.JsonSerializerOptions.Converters.Add(new ResultJsonConverterFactory()));

// For an in-process store such as a cache, never for a response: also write Metadata
var cacheOptions = new System.Text.Json.JsonSerializerOptions();
cacheOptions.Converters.Add(new ResultJsonConverterFactory(includeMetadata: true));

// Wire-format (success)
// {"isSuccess":true,"value":{...}}

// Wire-format (failure)
// {"isSuccess":false,"error":{"code":"USER.NOT_FOUND","message":"...","category":"NotFound","severity":"Error"}}
```

- The error object carries `code`, `message`, `category`, `severity`, `propertyName` for a validation error and `errors` for an `ErrorCollection`, so they survive a round trip. `metadata` is written only by a factory created with `includeMetadata: true`: `Metadata` is in-process context, never sent to a client by default. Its keys survive a round trip, its values come back as JSON primitives: an `int` as a `long`, a `Guid` or a `DateTime` as a `string`, another object as its string.
- Reading rebuilds an `ErrorCollection`, a `DeserializedValidationError` (which keeps `PropertyName`) or a `DeserializedError`.
- The non-generic `Result` carries its own reflection-free converter: returned as is from an endpoint, it answers `{"isSuccess":true}` or `{"isSuccess":false,"error":{...}}`, with no `metadata`, whether or not the factory is registered. Prefer `ToHttpResult()`, which answers 204 or problem details.
- Writing a `default` result throws `JsonException` with a clear message.
- Reading a payload that does not describe a valid result (a success without `value` or with `error`, a failure without `error` or with `value`, a duplicate `isSuccess`, an empty `code`) throws `JsonException`.

---

## Exception Boundaries

A technical error carries a generic, client-safe message. The exception itself goes to the log:

```csharp
using System.Data.Common;
using Microsoft.Extensions.Logging;

public sealed record DatabaseError()
    : Error(ErrorCode.From("INFRA.DATABASE.FAILURE"), "A database error occurred.")
{
    public override ErrorCategory Category => ErrorCategory.Technical; // explicit: an internal fault (MKR002)
}

public sealed record TechnicalError()
    : Error(ErrorCode.Internal, "An internal error occurred.")
{
    public override ErrorCategory Category => ErrorCategory.Technical; // explicit: an internal fault (MKR002)
}

public sealed record RemoteCallError()
    : Error(ErrorCode.ExternalServiceFailure, "The remote service call failed.")
{
    public override ErrorCategory Category => ErrorCategory.External;
}

public sealed record RemoteTimeoutError()
    : Error(ErrorCode.Timeout, "The remote service did not respond in time.")
{
    public override ErrorCategory Category => ErrorCategory.Timeout;
}

internal static partial class InfrastructureLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Database call failed")]
    public static partial void DatabaseCallFailed(this ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Remote service call failed")]
    public static partial void RemoteCallFailed(this ILogger logger, Exception exception);
}
```

For a call that takes no caller `CancellationToken`, such as a third-party client without one, use `Try` / `TryAsync` with a mapper. They catch every exception, a programming error included, and hand it to the mapper:

```csharp
IError ToDatabaseError(Exception ex)
{
    logger.DatabaseCallFailed(ex);
    return ex is DbException ? new DatabaseError() : new TechnicalError();
}

Result<User> result = Result<User>.Try(() => repository.GetUser(id), ToDatabaseError);

IError ToRemoteCallError(Exception ex)
{
    logger.RemoteCallFailed(ex);
    return ex switch
    {
        TaskCanceledException { InnerException: TimeoutException } => new RemoteTimeoutError(), // an HttpClient timeout
        HttpRequestException => new RemoteCallError(),
        _ => new TechnicalError(),
    };
}

Result<User> loaded = await Result<User>.TryAsync(
    async () => await legacyClient.FetchUserAsync(id), // a third-party API with no CancellationToken
    ToRemoteCallError);
```

For a call that takes one, catch only the infrastructure's exceptions at the boundary. Caller cancellation and programming errors then propagate; Microsoft's guidance is not to catch an exception your code cannot recover from:

```csharp
public async ValueTask<Result<User>> GetUserAsync(Guid id, CancellationToken ct = default)
{
    try
    {
        return Result.Success(await repository.GetUserAsync(id, ct).ConfigureAwait(false));
    }
    catch (DbException ex) when (!ct.IsCancellationRequested) // the ADO.NET provider's exception; caller cancellation, even one the provider throws as a DbException, and bugs propagate
    {
        logger.DatabaseCallFailed(ex);
        return Result.Failure<User>(new DatabaseError());
    }
}
```

Without a mapper, `Try` / `TryAsync` rethrow `OperationCanceledException`, and turn any other exception into an `ExceptionError`: code `EXCEPTION`, the generic message `An unexpected error occurred.`, no metadata. Its `Exception` property keeps the exception in process for logging and is not serialized.

With a mapper, the mapper receives every exception, `OperationCanceledException` included, and decides; the mappers above turn it into a `TechnicalError`, except an `HttpClient` timeout, which `ToRemoteCallError` turns into a `RemoteTimeoutError`. That is why `Try` / `TryAsync` are kept for calls that take no token.

---

## Exception Handling

`ResultException` is thrown when `.Value` is read on a failure or `.Error` on a success, when either one is read on an uninitialized (`default`) result — directly or by a method that reads it — and by `GetValueOrThrow()` on a failure without an exception factory:

```csharp
try
{
    int value = failedResult.GetValueOrThrow(); // throws ResultException
}
catch (ResultException ex)
{
    // ex.Errors contains the contributing IError list
    var errors = ex.Errors;
}
```

---

## Performance Notes

Measured with BenchmarkDotNet on one machine (below), on 2026-11-07. The figures are this machine's, not a promise for yours; run them with `dotnet run -c Release --project tests/MicroKit.Result.PerformanceTests -- --filter '*'`.

```
BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat) on WSL2
AMD Ryzen 9 7940HS w/ Radeon 780M Graphics 3.99GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.402, .NET 10.0.13, X64 RyuJIT x86-64-v4, DefaultJob
```

Compared libraries: ErrorOr 2.1.1, FluentResults 4.0.0. Each failure reuses a cached error, so the figures are the result's own cost.

| Creating a result | MicroKit.Result | ErrorOr | FluentResults |
|---|---|---|---|
| Success | below resolution (≈ 0 ns), 0 B | below resolution (≈ 0 ns), 0 B | 15.5 ns, 112 B |
| Failure | below resolution (≈ 0 ns), 0 B | 13.5 ns, 88 B | 18.5 ns, 120 B |

| `Map` → `Bind` → `Map`, lambdas without capture | MicroKit.Result | ErrorOr | FluentResults |
|---|---|---|---|
| Success rail | 5.3 ns, 0 B | 3.1 ns, 0 B | 180.9 ns, 832 B |
| Failure rail | 4.9 ns, 0 B | 3.1 ns, 0 B | 188.3 ns, 896 B |

`ValueTask<Result<T>>`, the async surface of the library and the module convention, against the same shape returning `Task<Result<T>>` (argument check, then an async core). The benchmarks return the `ValueTask` or `Task` itself, so the figures are the operation's own:

| Async case | `ValueTask` | `Task` |
|---|---|---|
| `MapAsync`, delegate completes synchronously | 12.6 ns, 72 B | 16.6 ns, 152 B |
| `MapAsync`, delegate truly yields | 583.7 ns, 246 B | 575.8 ns, 231 B |
| A port, completes synchronously | below resolution (≈ 0 ns), 0 B | 8.9 ns, 80 B |
| A port, truly yields | 562.0 ns, 120 B | 528.9 ns, 104 B |

When the operation completes synchronously, `ValueTask` is faster and allocates less (the 72 B of `MapAsync` are the delegate's own `Task.FromResult`); when it truly yields, `Task` is up to 6 % faster and allocates 6–13 % less. Which one the module's ports should return is a convention question, handled outside #184.

| `ErrorCollection.From` | 3 errors | 10 errors |
|---|---|---|
| As shipped: `Message` built with `string.Join` over LINQ | 67.8 ns, 280 B | 137.0 ns, 616 B |
| Candidate: each message read once into a pooled buffer, joined with `string.Create` (not shipped) | 54.7 ns, 232 B | 112.7 ns, 568 B |

The candidate was 18–19 % faster and allocated 8–17 % less. #184 Phase 7 changed the path only for a gain of at least 20 % on both sizes, on the failure rail, so the LINQ path stays.

What the figures confirm, on this machine: `Result.Success(value)` and `Result.Failure<T>(error)` do not allocate; `Map` and `Bind` do not allocate on either rail when their lambdas capture nothing. Not measured: a capturing lambda, an `Ensure` whose error is built on every call, a `Where` or a LINQ query, which may allocate on every call, and the effect of `ThrowHelper`'s `[DoesNotReturn]` / `[MethodImpl(NoInlining)]`.

---

## Packages

| Package | Description |
|---------|-------------|
| `MicroKit.Result` | Core — zero dependencies |
| `MicroKit.Result.AspNetCore` | `ToHttpResult()`, `ProblemDetails` factory |
| `MicroKit.Result.Analyzers` | Roslyn analyzers, build time only: `MKR001` (a discarded result), `MKR002` (an error that keeps the default `Technical` category) |

---

## License

`MicroKit.Result` and `MicroKit.Result.AspNetCore` are MIT-licensed.
