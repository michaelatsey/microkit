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

Every operation stays on one of these rails. There are no surprises, no hidden throws, no stack unwinding for predictable outcomes.

**Key design principles:**
- `Result<T>` is a `readonly struct` — zero heap allocation on the success path
- `byte _tag` discriminator: no `bool` ambiguity, safe with `default`
- `sealed` by default; `ThrowHelper` keeps hot paths JIT-inlineable
- NativeAOT / trim-safe (reflection only in `MicroKit.Result.Serialization`)

---

## Installation

```bash
dotnet add package MicroKit.Result
dotnet add package MicroKit.Result.AspNetCore  # optional — Minimal APIs / MVC
```

---

## Quick Start

```csharp
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

Result<int> value  = Result.Success(42);
Result<int> failed = Result.Failure<int>(new MyError());

// implicit conversion from T
Result<int> implicit = 42;
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
ErrorCode.NotFound          // "NOT_FOUND"
ErrorCode.Validation        // "VALIDATION"
ErrorCode.Timeout           // "TIMEOUT"
ErrorCode.ExternalServiceFailure // "EXTERNAL_SERVICE_FAILURE"
// ...19 predefined constants total

// Comparable
ErrorCode.Conflict < ErrorCode.NotFound  // ordinal comparison
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

Aggregates multiple errors into one `IError`:

```csharp
var collection = ErrorCollection.From(error1, error2, error3);

// Filter
collection.WithCategory(ErrorCategory.Validation)
collection.WithSeverity(ErrorSeverity.Critical)
collection.WithCode(ErrorCode.NotFound)
collection.OfType<ValidationError>()
collection.HasCategory(ErrorCategory.Validation)  // bool

// Group
IReadOnlyDictionary<ErrorCategory, IReadOnlyList<IError>> byCategory =
    collection.GroupByCategory();

// Flatten nested collections
ErrorCollection flat = collection.Flatten();
```

### IError extension methods

```csharp
error.IsCategory(ErrorCategory.Validation)
error.IsCritical()
error.IsWarning()
error.IsInformation()
error.HasMetadata("correlationId")
error.TryGetMetadata<string>("correlationId")
error.ToException()   // wraps in ResultException
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

```csharp
result
    .Tap(v => logger.LogInformation("Value: {V}", v))
    .TapError(e => metrics.RecordFailure(e.Code));
```

### Ensure — guard on success

```csharp
Result<User> active = GetUser(id)
    .Ensure(u => u.IsActive, new UserInactiveError())
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

string s = result.GetValueOrThrow();                          // ResultException on failure
string s = result.GetValueOrThrow(e => new MyException(e));  // custom exception

if (result.TryGetValue(out var v))
    Process(v);
```

### Finally — always-execute cleanup

```csharp
result.Finally(r => logger.LogInformation("Done, success={S}", r.IsSuccess));
```

### Conversion

```csharp
Result nonGeneric = result.ToResult();  // drops the value, keeps the error
```

---

## Async

All sync methods have async counterparts on three surfaces:

```csharp
// Surface 1: async delegate on Result<T>
await result.MapAsync(async v => await TransformAsync(v));
await result.BindAsync(async v => await GetNextAsync(v));
await result.CompensateAsync(async e => await FallbackAsync(e));
await result.FinallyAsync(async r => await LogAsync(r));

// Surface 2: on Task<Result<T>> (sync delegate — awaits the task first)
await GetResultTask().Map(v => Transform(v));
await GetResultTask().Bind(v => GetNext(v));

// Surface 3: on ValueTask<Result<T>> (sync delegate)
await GetResultValueTask().Map(v => Transform(v));
```

All async methods use `ConfigureAwait(false)`.

---

## Validation

### ValidationError factories

```csharp
ValidationError.Required("Email")
ValidationError.MinLength("Password", 8)
ValidationError.MaxLength("Username", 50)
ValidationError.StringLength("Bio", 10, 500)
ValidationError.OutOfRange("Age", min: 0, max: 120)
ValidationError.InvalidFormat("PostalCode", hint: "5 digits")
ValidationError.InvalidEmail()
ValidationError.InvalidUrl("WebsiteUrl")
ValidationError.Custom("Field", "Custom message")
```

### ValidationResult — collect all errors

```csharp
var validation = new ValidationResult()
    .AddErrorIf(string.IsNullOrEmpty(name), "Name", "Name is required.")
    .AddErrorIf(age < 0, "Age", "Age must be non-negative.")
    .AddError(ValidationError.InvalidEmail());

Result<Order> result = validation.IsValid
    ? CreateOrder(...)
    : validation.ToResult<Order>(default!);
```

---

## Combining Results

```csharp
// Fail-fast — stops at first failure
Result combined = Result.Combine(validateName, validateEmail, validateAge);

// Collect-all — returns ErrorCollection with ALL errors
Result allErrors = Result.CombineAll(validateName, validateEmail, validateAge);

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
IEnumerable<T>      values = results.Successes();
IEnumerable<IError> errors = results.Failures();

// Map each element, fail-fast on first error
Result<IReadOnlyList<Dto>> dtos = users.Traverse(u => MapToDto(u));
```

---

## ASP.NET Core

```csharp
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

---

## Serialization

```csharp
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

For infrastructure calls that can throw, use `Try` / `TryAsync`:

```csharp
// Default — wraps Exception in ExceptionError (Technical category)
Result<User> result = Result.Try(() => repository.GetUser(id));

// Custom mapper — convert exception to your domain error
Result<User> result = Result<User>.TryAsync(
    async () => await _db.Users.FindAsync(id),
    ex => new DatabaseError(ex));
```

---

## Exception Handling

`ResultException` is thrown only when you access `.Value` on a failure or `.Error` on a success:

```csharp
try
{
    var value = failedResult.Value; // throws ResultException
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

MIT — see [LICENSE](../../LICENSE).
