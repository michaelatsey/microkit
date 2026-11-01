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
- `Result<T>` is a `readonly struct` — zero heap allocation on the success path
- `byte _tag` discriminator: no `bool` ambiguity, safe with `default`
- `sealed` by default; `ThrowHelper` keeps hot paths JIT-inlineable
- NativeAOT / trim-safe core; reflection is confined to the JSON converters (`MicroKit.Result.Serialization` namespace, annotated `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`)

---

## Installation

```bash
dotnet add package MicroKit.Result
dotnet add package MicroKit.Result.AspNetCore  # optional — Minimal APIs / MVC
```

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
```

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

### ErrorCode

Strongly-typed hierarchical code: `DOMAIN.ENTITY.ACTION`

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

| Category | HTTP | Description |
|----------|------|-------------|
| `NotFound` | 404 | Resource does not exist |
| `Validation` | 422 | Input validation failed |
| `BusinessRule` | 422 | Domain business rule violated |
| `Unauthorized` | 401 | Authentication required |
| `Forbidden` | 403 | Access denied |
| `Conflict` | 409 | Resource conflict |
| `TooManyRequests` | 429 | Rate limit exceeded |
| `Timeout` | 408 | Operation timed out |
| `Cancelled` | 499 | Request cancelled by caller |
| `PreconditionFailed` | 412 | Precondition not met |
| `External` | 502 | External dependency failed |
| `NotSupported` | 501 | Operation not supported |
| `Unavailable` | 503 | Service temporarily unavailable |
| `Technical` | 500 | Internal error (default) |

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

Aggregates multiple errors into one `IError`. A filter that matches nothing currently returns an empty collection, which `From` forbids; #184 Phase 2 changes it.

```csharp
var collection = ErrorCollection.From(error1, error2, error3);

// Filter
ErrorCollection validation = collection.WithCategory(ErrorCategory.Validation);
ErrorCollection critical = collection.WithSeverity(ErrorSeverity.Critical);
ErrorCollection notFound = collection.WithCode(ErrorCode.NotFound);
ErrorCollection typed = collection.OfType<ValidationError>();
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

### MapError — transform the error

```csharp
Result<Order> result = GetOrder(id)
    .MapError(e => new EnrichedError(e, correlationId));
```

### Compensate — fallback on failure

```csharp
Result<Product> product = GetProduct(id)
    .Compensate(_ => GetDefaultProduct());
```

### ValueAccess — safe value retrieval

```csharp
int value = result.GetValueOrDefault(0);

int strict = result.GetValueOrThrow();                          // ResultException on failure
int custom = result.GetValueOrThrow(e => new MyException(e));  // custom exception

if (result.TryGetValue(out var v))
    Process(v);
```

### Finally — always-execute cleanup

```csharp
Result<int> completed = result.Finally(r => logger.Completed(r.IsSuccess));
```

### Conversion

```csharp
Result nonGeneric = result.ToResult();  // drops the value, keeps the error
```

---

## Async

The nine pipeline operations of `Result<T>` (`Map`, `Bind`, `Match`, `Tap`, `TapError`, `Ensure`, `MapError`, `Compensate`, `Finally`) have async counterparts on three surfaces. The non-generic `Result` currently has none; #184 Phase 4 adds them.

```csharp
// Surface 1: async delegate on Result<T>
Result<string> mapped = await result.MapAsync(async v => await TransformAsync(v));
Result<string> chained = await result.BindAsync(async v => await GetNextAsync(v));
Result<int> recovered = await result.CompensateAsync(async e => await FallbackAsync(e));
Result<int> finished = await result.FinallyAsync(async r => await LogAsync(r));

// Surface 2: on Task<Result<T>> (sync delegate — awaits the task first)
Result<string> mappedTask = await GetResultTask().Map(v => Transform(v));
Result<string> chainedTask = await GetResultTask().Bind(v => GetNext(v));

// Surface 3: on ValueTask<Result<T>> (sync delegate)
Result<string> mappedValueTask = await GetResultValueTask().Map(v => Transform(v));
```

All async methods use `ConfigureAwait(false)`.

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
    .AddError(ValidationError.InvalidEmail());

Result<Order> result = validation.IsValid
    ? CreateOrder(name, age)
    : validation.ToResult<Order>(default!);
```

---

## Combining Results

```csharp
// Fail-fast — stops at first failure
Result combined = ResultCombineExtensions.Combine(validateName, validateEmail, validateAge);

// Collect-all — returns ErrorCollection with ALL errors
Result allErrors = ResultCombineExtensions.CombineAll(validateName, validateEmail, validateAge);

// Tuple combination (up to 3)
Result<(User, Cart)> pair = ResultCombineExtensions.Combine(getUser, getCart);
```

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

---

## ASP.NET Core

```csharp
using MicroKit.Result;
using MicroKit.Result.AspNetCore;
using Microsoft.AspNetCore.Builder;

// Minimal API
app.MapGet("/users/{id}", async (Guid id, IUserService svc) =>
    (await svc.GetUserAsync(id)).ToHttpResult());

// Result without value (command)
app.MapDelete("/users/{id}", async (Guid id, IUserService svc) =>
    (await svc.DeleteUserAsync(id)).ToHttpResult());
```

`ToHttpResult()` maps `ErrorCategory` to the correct HTTP status code and produces an RFC 9457 `ProblemDetails` body on failure:

```json
{
  "type": "https://httpstatuses.io/404",
  "title": "Not Found",
  "status": 404,
  "detail": "User abc123 not found",
  "errorCode": "USER.NOT_FOUND"
}
```

Current behavior that #184 changes:

- `detail` is `IError.Message`. For the error `Try` / `TryAsync` build without a mapper, that is `Exception.Message` (see [Exception Boundaries](#exception-boundaries)); Phase 2 removes it from the response.
- `errors` lists only `code` and `message`, and only for an `ErrorCollection`, so a single `ValidationError` emits none; Phase 2 groups validation errors by property.
- An `ErrorCollection` answers with its first error's category; Phase 2 makes it the shared category, else the first that maps to 5xx, else the first.
- `Timeout` answers 408, `type` links to a third-party site, and results are returned as `IResult`; Phase 5 decides these.

---

## Serialization

```csharp
using MicroKit.Result.Serialization;

// Register globally
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new ResultJsonConverterFactory()));

// Wire-format (success)
// {"isSuccess":true,"value":{...}}

// Wire-format (failure)
// {"isSuccess":false,"error":{"code":"USER.NOT_FOUND","message":"...","category":"NotFound"}}
```

---

## Exception Boundaries

A technical error carries a generic, client-safe message. The exception itself goes to the log:

```csharp
using Microsoft.Extensions.Logging;

public sealed record DatabaseError()
    : Error(ErrorCode.From("INFRA.DATABASE.FAILURE"), "A database error occurred.");

internal static partial class InfrastructureLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Database call failed")]
    public static partial void DatabaseCallFailed(this ILogger logger, Exception exception);
}
```

For a call that takes no caller `CancellationToken`, use `Try` / `TryAsync` with a mapper:

```csharp
IError ToDatabaseError(Exception ex)
{
    logger.DatabaseCallFailed(ex);
    return new DatabaseError();
}

Result<User> result = Result<User>.Try(() => repository.GetUser(id), ToDatabaseError);

Result<User> loaded = await Result<User>.TryAsync(
    async () => await repository.GetUserAsync(id),
    ToDatabaseError);
```

For a call that takes one, catch at the boundary so that caller cancellation propagates:

```csharp
public async ValueTask<Result<User>> GetUserAsync(Guid id, CancellationToken ct = default)
{
    try
    {
        return Result.Success(await repository.GetUserAsync(id, ct).ConfigureAwait(false));
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception ex)
    {
        logger.DatabaseCallFailed(ex);
        return Result.Failure<User>(new DatabaseError());
    }
}
```

Current behavior that #184 Phase 2 changes:

- Every `Try` / `TryAsync` overload catches `OperationCanceledException`. Phase 2 makes the overloads without a mapper rethrow it; with a mapper, the mapper decides.
- Without a mapper, the error's message is `Exception.Message`, and `ToHttpResult()` writes it to `ProblemDetails.Detail`. Phase 2 replaces it with a generic message.

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

- `Result<T>` is a `readonly struct` — passes by value, no heap allocation
- `byte _tag` discriminator: `default(Result<T>)` is an uninitialized state that throws on access
- `Map` and `Bind` are zero-allocation on the success path when lambdas don't capture
- `ErrorCollection` uses `ImmutableArray<IError>` for zero-copy iteration
- `ThrowHelper` methods are `[MethodImpl(NoInlining)]` — cold paths stay off the hot path
- `ConfigureAwait(false)` on all async paths to avoid context capture overhead

---

## Packages

| Package | Description |
|---------|-------------|
| `MicroKit.Result` | Core — zero dependencies |
| `MicroKit.Result.AspNetCore` | `ToHttpResult()`, `ProblemDetails` factory |

---

## License

`MicroKit.Result` and `MicroKit.Result.AspNetCore` are MIT-licensed.
