# MicroKit.Result usage guide

MicroKit.Result makes the outcome of an operation explicit. A method returns a `Result` or a `Result<T>`: a success, which holds a value, or a failure, which holds a typed error. Callers compose results on two rails and handle the failure where it matters, without exceptions for expected outcomes.

Use it when a failure is predictable: a missing entity, invalid input, a broken business rule, a dependency that is down. Keep exceptions for the unpredictable: a bug, a broken invariant, an exhausted resource, a cancellation the caller requested.

This guide covers .NET 10 and the three packages: `MicroKit.Result`, `MicroKit.Result.AspNetCore` and `MicroKit.Result.Analyzers`. Every C# block on this page is compiled from [`samples/MicroKit.Result.Samples/Guide`](../../samples/MicroKit.Result.Samples/Guide).

## Contents

- [Install the packages](#install-the-packages)
- [Return a result](#return-a-result)
- [Compose results](#compose-results)
- [Compose asynchronously](#compose-asynchronously)
- [Define errors](#define-errors)
- [Validate input](#validate-input)
- [Combine results](#combine-results)
- [Handle exceptions at a boundary](#handle-exceptions-at-a-boundary)
- [Return results over HTTP](#return-results-over-http)
- [Serialize results to JSON](#serialize-results-to-json)
- [Catch mistakes with the analyzers](#catch-mistakes-with-the-analyzers)
- [Test code that returns results](#test-code-that-returns-results)
- [Troubleshooting](#troubleshooting)

## Install the packages

The packages target .NET 10 and are published as previews.

```bash
dotnet add package MicroKit.Result --prerelease
dotnet add package MicroKit.Result.AspNetCore --prerelease  # optional: Minimal APIs and MVC
dotnet add package MicroKit.Result.Analyzers --prerelease   # recommended: build-time checks
```

`MicroKit.Result` has no dependency. It is trim and NativeAOT compatible, checked by the trim and AOT analyzers, except for `ResultJsonConverterFactory` and `ResultJsonConverter<T>`, which use reflection and carry `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`.

`MicroKit.Result.AspNetCore` maps results to HTTP responses. Add it only to the project that hosts the endpoints.

`MicroKit.Result.Analyzers` is a development dependency: it runs at build time and does not flow to the projects that depend on yours. Add it to every project that returns results.

## Return a result

A method that can fail returns a result instead of throwing. `Result` carries no value: use it for a command. `Result<T>` carries a value on success: use it for a query or a transformation. `Unit` stands for "no value" where a type argument is required.

```csharp
Result saved = Result.Success();
Result rejected = Result.Failure(new OrderLockedError(orderId));

Result<int> count = Result.Success(42);
Result<int> missing = Result.Failure<int>(new OrderLockedError(orderId));

// A value converts to a success, an error to a failure
Result<int> fromValue = 42;
Result<int> fromError = new OrderLockedError(orderId);
Result commandFailed = new OrderLockedError(orderId);

// Unit stands for "no value" where a type argument is required
Result<Unit> acknowledged = Result.Success(Unit.Value);
```

`OrderLockedError` is a domain error. [Define errors](#define-errors) shows how to declare one.

The implicit conversions keep a method short. A value converts to a success. An `Error` or an `ErrorCollection` converts to a failure.

```csharp
public Result<Order> FindOrder(Guid id) =>
    _orders.TryGetValue(id, out Order? order)
        ? Result.Success(order)
        : Error.NotFound(ErrorCode.From("ORDER.NOT_FOUND"), $"Order {id} does not exist.");

public Result Cancel(Order order)
{
    if (order.IsShipped)
        return new OrderLockedError(order.Id);

    _orders.Remove(order.Id);
    return Result.Success();
}
```

A success always holds a value. `Success(null)` and the conversion from a `null` value throw `ArgumentNullException`: absence is a failure, such as `Error.NotFound`, not a null value.

A result is never the value of another result. When `T` is `object`, `Success` and the conversion from `T` throw `ArgumentException` on a value that is itself a result, so a failure never becomes a success.

An expression typed `IError` does not convert, because C# has no user-defined conversion from an interface. Call `Result.Failure` or `Result.Failure<T>` for it.

The conversion from an error depends on `T` when `T` is itself an error type. Into `Result<Error>` or `Result<ErrorCollection>`, it is ambiguous and does not compile (CS0457). Into a `Result` of a subtype of `Error`, the conversion from `T` wins and gives a success. Write `Success` or `Failure` explicitly there.

Never build a result with `new Result<T>()` or `default`. That result is neither a success nor a failure: `IsSuccess` and `IsFailure` are both `false`, and reading `Value` or `Error` throws `ResultException`.

## Compose results

The operations of this section run on one rail and pass the other through unchanged. A failure skips every success operation until something consumes it.

### Transform the value with Map

`Map` transforms the value of a success. A failure passes through.

```csharp
Result<string> name = GetUser(id).Map(u => u.Name);
```

The mapper must return a value. A mapper that can return `null` belongs in `Bind`, returning a failure.

### Chain operations with Bind

`Bind` chains an operation that itself returns a result. The first failure stops the chain.

```csharp
Result<Invoice> invoice = GetUser(id)
    .Bind(u => GetCart(u.CartId))
    .Bind(c => CreateInvoice(c));
```

Use `Map` when the next step cannot fail, and `Bind` when it can. A `Map` whose delegate returns a result throws `ArgumentException`: that step needs `Bind`.

### Check a condition with Ensure

`Ensure` turns a success into a failure when a condition does not hold.

```csharp
Result<User> active = GetUser(id)
    .Ensure(u => u.IsActive, new UserInactiveError(id))
    .Ensure(u => u.IsEmailVerified, new EmailNotVerifiedError());
```

`Ensure` takes the error already built, so the error is built on every call, success included. When the error reads the value, or costs to build, use `Bind`, which builds it only on failure:

```csharp
Result<User> activeLazy = GetUser(id)
    .Bind(u => u.IsActive ? Result.Success(u) : new UserInactiveError(u.Id));
```

### Consume both rails with Match

`Match` ends a pipeline. It runs one function per rail and returns what that function returns.

```csharp
string label = result.Match(
    onSuccess: v => $"Got {v}",
    onFailure: e => $"Error: {e.Message}");
```

### Run side effects with Tap and TapError

`Tap` runs an action on the value of a success. `TapError` runs one on the error of a failure. Both return the result unchanged. Log through source-generated `[LoggerMessage]` methods:

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

A side effect does not transform. It never changes the value or the object the value refers to. A transformation goes through `Map` or `Bind`, which return the new value.

### Work on the failure rail with MapError and Compensate

`MapError` replaces the error and stays on the failure rail. `Compensate` returns to the success rail with a fallback.

```csharp
// MapError: replace the error, stay on the failure rail
Result<Order> enriched = GetOrder(id)
    .MapError(e => new EnrichedError(e, correlationId));

// Compensate: return to the success rail with a fallback
Result<Product> product = GetProduct(id)
    .Compensate(_ => GetDefaultProduct());
```

`EnrichedError` is an application type. A wrapper forwards the `Category`, `Severity` and `Metadata` of the error it wraps. Without them it would be a `Technical` error, and HTTP would answer 500.

```csharp
// A wrapper forwards the wrapped error's category, severity and metadata
internal sealed record EnrichedError(IError Inner, string CorrelationId) : Error(Inner.Code, Inner.Message)
{
    public override ErrorCategory Category => Inner.Category;

    public override ErrorSeverity Severity => Inner.Severity;

    public override IReadOnlyDictionary<string, object?> Metadata =>
        new ErrorMetadataBuilder().Merge(Inner.Metadata).WithCorrelationId(CorrelationId).Build();
}
```

### Read the value

Read `Value` only once you know the result is a success. `TryGetValue` checks and reads in one call, which suits an early return:

```csharp
Result<User> result = GetUser(id);
if (!result.TryGetValue(out User? user))
    return $"Unavailable: {result.Error.Message}";

return $"{user.Name}, active: {user.IsActive}";
```

`GetValueOrDefault` returns a fallback on failure. Use it when the failure really means "use this value instead":

```csharp
string label = GetUser(id).Map(u => u.Name).GetValueOrDefault("Guest");
```

`GetValueOrThrow` throws `ResultException` on a failure, or the exception its factory returns. Keep it for a failure that would be a bug.

### Run code on either rail with Finally

`Finally` runs an action with the whole result, whatever its rail, and returns the result unchanged.

```csharp
Result<int> completed = result.Finally(r => logger.Completed(r.IsSuccess));
```

`Finally` runs only when a result reaches it. An exception thrown earlier in the chain skips it, so it does not replace `try` / `finally` for cleanup.

### Compose commands

The non-generic `Result` has the same operations: `Map`, `Bind`, `Ensure`, `Match`, `Tap`, `TapError`, `MapError`, `Compensate` and `Finally`. Its delegates take no value.

```csharp
Result shipped = CheckAddress(order)
    .Ensure(() => order.IsPaid, new OrderNotPaidError(order.Id))
    .Bind(() => Ship(order))
    .TapError(e => metrics.RecordFailure(e.Code));

string status = shipped.Match(
    onSuccess: () => "Shipped",
    onFailure: e => e.Message);

// From a value to a command
Result archived = GetOrder(order.Id).Bind(o => Archive(o));
Result checkedOnly = GetOrder(order.Id).ToResult(); // keeps the error, drops the value
```

### Use LINQ query syntax

Query syntax is `Bind` and `Map` written as `from` and `select`. Each `from` runs only when the previous one succeeded.

```csharp
Result<OrderDto> dto =
    from user in GetUser(id)
    from cart in GetCart(user.CartId)
    from order in CreateOrder(cart)
    select order.ToDto();

// where needs an error for the false case: use Where, the method form
Result<int> positive = Result.Success(42)
    .Where(v => v > 0, new NegativeValueError());

// AsEnumerable: one element on success, none on failure
foreach (OrderDto value in dto.AsEnumerable())
    Process(value);
```

## Compose asynchronously

Each operation of [Compose results](#compose-results) has an async counterpart, `MapAsync`, `BindAsync` and so on, on `Result<T>` and on `Result`. Each returns a `ValueTask`, uses `ConfigureAwait(false)`, and validates its arguments when called, before anything is awaited.

### Await an async delegate

An async lambda with one parameter binds to the overload whose delegate returns a `Task`.

```csharp
Result<string> mapped = await result.MapAsync(async v => await TransformAsync(v));
Result<string> chained = await result.BindAsync(async v => await GetNextAsync(v));
Result<int> recovered = await result.CompensateAsync(async e => await FallbackAsync(e));
Result<int> finished = await result.FinallyAsync(async r => await LogAsync(r));

// Tap, TapError and Finally given an async lambda await the side effect
Result<int> audited = await result.Tap(async v => await AuditAsync(v));
```

`Tap`, `TapError` and `Finally` given a delegate that returns a `Task` bind to an overload that awaits it and returns a `ValueTask`. An `Action` keeps the synchronous overload.

A lambda that is not `async` and returns a `ValueTask` binds to the `Action` overload, which discards its `ValueTask`; CA2012 reports it. Write it `async x => await port(x)`.

### Continue a task of a result

On `Task<Result<T>>` and `ValueTask<Result<T>>`, the operations take a synchronous delegate and apply it once the task completes.

```csharp
// On Task<Result<T>> and ValueTask<Result<T>>: a synchronous delegate, applied once the task completes
Result<string> mappedTask = await GetResultTask().Map(v => Transform(v));
Result<string> chainedTask = await GetResultTask().Bind(v => GetNext(v));
Result<string> mappedValueTask = await GetResultValueTask().Map(v => Transform(v));

// An async side effect is awaited on these surfaces too
Result<int> auditedTask = await GetResultTask().Tap(async v => await AuditAsync(v));
```

To use another async delegate, await the task first, then call `MapAsync`, `BindAsync` and the others on the result.

### Pass a cancellation token

Each `XxxAsync` operation has a second form for a port such as `ValueTask<Result<User>> GetUserAsync(Guid id, CancellationToken ct = default)`. Its delegate takes the token and returns a `ValueTask`. The operation takes `CancellationToken ct = default` as its last parameter and passes it to the delegate.

```csharp
// The token form: the delegate takes the token and returns a ValueTask, as a port does
Result<string> fetched = await result.BindAsync((v, token) => FetchAsync(v, token), ct);

// The non-generic Result has the same operations, in both forms
Result saved = await command.BindAsync(async () => await SaveAsync());
Result audit = await command.TapAsync(token => AuditCommandAsync(token), ct);
```

The operations neither check the token nor catch a cancellation. A cancellation the caller requested propagates as an `OperationCanceledException`; it never becomes an error.

Await a `ValueTask` once, directly. Never block on it with `.Result` or `.GetAwaiter().GetResult()`.

## Define errors

An error says what went wrong in terms the domain understands. Every error implements `IError`: a `Code`, a `Message`, a `Category`, a `Severity` and `Metadata`. Each has one role:

- `Code` identifies the error. It is stable: clients and logs can branch on it.
- `Message` describes the error to a person. It reaches the client, so it never carries internal details.
- `Category` says what kind of failure happened. It alone decides the HTTP status.
- `Severity` serves logging and observability. It changes no status and no control flow.
- `Metadata` is in-process context: correlation ids, entity ids, retry hints. No response writes it by default.

### Declare a domain error

Give each domain error its own type. Derive a `sealed record` from `Error` and override `Category`.

```csharp
public sealed record UserNotFoundError(Guid UserId)
    : Error(ErrorCode.From("USER.NOT_FOUND"), $"User {UserId} not found")
{
    public override ErrorCategory Category => ErrorCategory.NotFound;
}
```

An error that does not override `Category` is `Technical`, an internal fault, which HTTP answers with 500. The analyzer [MKR002](#mkr002-an-error-keeps-the-default-category) reports it.

The library ships few error types: `ValidationError`, `ErrorCollection`, `CategorizedError`, `ExceptionError`, and the errors the JSON converters read back. Every domain error is yours.

### Build an ad hoc error with a factory

At a boundary, or for an error used once, a factory of `Error` builds a `CategorizedError` with an explicit category. There is one factory per category: `Error.NotFound`, `Error.Conflict`, `Error.Timeout` and so on.

```csharp
// An ad hoc error with an explicit category: one factory per ErrorCategory
CategorizedError missing = Error.NotFound(ErrorCode.From("ORDER.NOT_FOUND"), $"Order {orderId} does not exist.");
return missing; // the implicit conversion makes it a failure
```

A factory rejects an empty message and an uninitialized code with `ArgumentException`. Two `CategorizedError` instances with the same code, message and category are equal.

### Choose a category

| Category | The failure |
|---|---|
| `NotFound` | The requested resource does not exist. |
| `Validation` | The input is invalid. |
| `Unauthorized` | The caller is not authenticated. |
| `Forbidden` | The authenticated caller may not perform the operation. |
| `Conflict` | The operation conflicts with the current state of a resource. |
| `TooManyRequests` | The caller exceeded a rate limit. |
| `BusinessRule` | A business rule of the domain forbids the operation. |
| `PreconditionFailed` | A precondition of the operation was not met. |
| `Technical` | An internal fault of the system. The default. |
| `Unavailable` | A service the operation needs is temporarily unavailable. |
| `External` | An external dependency returned a failure. |
| `NotSupported` | The operation is not supported. |
| `Timeout` | The operation exceeded its time limit. |
| `Cancelled` | Something other than the caller cancelled the operation, such as a downstream service. |

A cancellation the caller requested is never a `Cancelled` error. It propagates as an `OperationCanceledException`.

[Return results over HTTP](#status-codes-by-category) maps each category to a status.

### Name errors with ErrorCode

A code is a string, `DOMAIN.ENTITY.REASON` by convention, such as `AUTH.USER.NOT_FOUND`. `ErrorCode.From` rejects an empty or whitespace value.

```csharp
var code = ErrorCode.From("ORDER.PAYMENT.DECLINED");
string raw = code; // implicit conversion to string

// Predefined convenience codes, for an error without a domain code
ErrorCode notFound = ErrorCode.NotFound;               // "NOT_FOUND"
ErrorCode timeout = ErrorCode.Timeout;                 // "TIMEOUT"
ErrorCode external = ErrorCode.ExternalServiceFailure; // "EXTERNAL_SERVICE_FAILURE"

// Ordinal comparison
bool ordered = ErrorCode.Conflict < ErrorCode.NotFound;
```

The predefined codes are not a second taxonomy. `Category`, not `Code`, drives behavior.

### Add severity and metadata

Override `Severity` and `Metadata` when an error needs them. `ErrorMetadataBuilder` builds the metadata: `Add`, `Merge`, `WithCorrelationId`, `WithTraceId` and `WithTimestamp`, then `Build`.

```csharp
public sealed record PaymentDeclinedError(Guid PaymentId, string ProviderReference)
    : Error(ErrorCode.From("PAYMENT.CARD.DECLINED"), "The card was declined.")
{
    public override ErrorCategory Category => ErrorCategory.BusinessRule;

    public override ErrorSeverity Severity => ErrorSeverity.Warning;

    // In-process context for logs and traces; no response writes it by default
    public override IReadOnlyDictionary<string, object?> Metadata =>
        new ErrorMetadataBuilder()
            .Add("paymentId", PaymentId)
            .Add("providerReference", ProviderReference)
            .WithTraceId()
            .Build();
}
```

Put internal details in the log or in `Metadata`, never in `Message`.

To add context to an existing error, wrap it, as [`EnrichedError`](#work-on-the-failure-rail-with-maperror-and-compensate) does.

### Inspect an error

Extension methods on `IError` answer the common questions.

```csharp
bool isValidation = error.IsCategory(ErrorCategory.Validation);
bool isCritical = error.IsCritical();
bool isWarning = error.IsWarning();
bool hasPayment = error.HasMetadata("paymentId");
string? reference = error.TryGetMetadata<string>("providerReference");
```

To branch on a specific error, match its type: `if (result.IsFailure && result.Error is UserNotFoundError notFound)`.

## Validate input

A validation error belongs to a field. `ValidationError` carries the `PropertyName`, the category `Validation`, the severity `Warning`, and the code `VALIDATION.<PROPERTY>`, such as `VALIDATION.EMAIL`.

### Build validation errors

The factories of `ValidationError` write the message for you.

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

### Return a single validation error

A `ValidationError` converts to a failure, like any `Error`.

```csharp
internal static Result<EmailAddress> ParseEmail(string input)
{
    if (string.IsNullOrWhiteSpace(input))
        return ValidationError.Required("Email"); // one validation error converts to a failure
    if (!input.Contains('@'))
        return ValidationError.InvalidEmail();

    return new EmailAddress(input);
}
```

### Collect every validation error

`ValidationResult` collects errors, so the caller sees all of them at once. `ToResult<T>()` returns a failure that holds an `ErrorCollection` of every error.

```csharp
var validation = new ValidationResult()
    .AddErrorIf(string.IsNullOrEmpty(name), "Name", "Name is required.")
    .AddErrorIf(age < 0, "Age", "Age must be non-negative.")
    .AddErrorIf(!email.Contains('@'), "Email", "Email is not a valid email address.");

if (!validation.IsValid)
    return validation.ToResult<Order>(); // a failure holding every validation error

return CreateOrder(name, age, email);
```

`ToResult<T>()` returns a failure only. On a valid `ValidationResult` it throws `InvalidOperationException`, so call it after checking `IsValid`.

`ToResult<T>(value)` returns the success or the failure. `ToResult()` does the same for a command.

```csharp
return new ValidationResult()
    .AddErrorIf(string.IsNullOrWhiteSpace(email), "Email", "Email is required.")
    .AddErrorIf(password.Length < 8, "Password", "Password must be at least 8 characters.")
    .ToResult(new Registration(email, password)); // the value on success, every error on failure
```

`AddError(propertyName, message)` and `AddError(error)` add an error unconditionally.

## Combine results

### Combine independent results

`Result.Combine` returns the first failure. `Result.CombineAll` collects every error into one `ErrorCollection`. The tuple overloads of `Combine` gather the values of two or three results.

```csharp
// Fail fast: the first failure
Result combined = Result.Combine(validateName, validateEmail, validateAge);

// Collect all: one ErrorCollection holding every error
Result allErrors = Result.CombineAll(validateName, validateEmail, validateAge);

// Values into a tuple, for two or three results
Result<(User, Cart)> pair = Result.Combine(getUser, getCart);
```

Every argument is evaluated before the call. To skip a step after a failure, chain it with `Bind`: `Combine(Validate(x), Save(x))` runs `Save` even when `Validate` fails.

### Work with an ErrorCollection

An `ErrorCollection` is an `IError` that holds other errors. It is never empty. Its code is `MULTI.ERROR`, its message joins the messages with `; `, and its severity is the highest of its errors, at least `Error`.

Its category is the category its errors share; otherwise the one of highest precedence, so the order of the errors never matters. Precedence, highest first: faults of the system (`Technical`, `External`, `Unavailable`, `Timeout`, `NotSupported`, undefined values), then `Unauthorized` and `Forbidden`, then `NotFound`, `Conflict`, `PreconditionFailed` and `TooManyRequests`, then `Cancelled`, then `Validation` and `BusinessRule`; inside a group, the category declared first in `ErrorCategory` wins. A server fault is never hidden behind a client error, nor an access error behind a validation error.

```csharp
var collection = ErrorCollection.From(error1, error2, error3);

// A filter may match nothing, so it returns a list, never an empty collection
IReadOnlyList<IError> validation = collection.WithCategory(ErrorCategory.Validation);
IReadOnlyList<IError> critical = collection.WithSeverity(ErrorSeverity.Critical);
IReadOnlyList<IError> notFound = collection.WithCode(ErrorCode.NotFound);
IReadOnlyList<ValidationError> typed = collection.OfType<ValidationError>();
bool hasValidation = collection.HasCategory(ErrorCategory.Validation);

// Group
IReadOnlyDictionary<ErrorCategory, IReadOnlyList<IError>> byCategory = collection.GroupByCategory();
IReadOnlyDictionary<ErrorCode, IReadOnlyList<IError>> byCode = collection.GroupByCode();

// Flatten nested collections
ErrorCollection flat = collection.Flatten();
```

`ErrorCollection.From` throws `ArgumentException` when it receives no error.

### Process a sequence of results

```csharp
List<Result<Order>> results = orderIds.Select(FindOrder).ToList();

// Split into values and errors
var (orders, errors) = results.Partition();

// Only the values, or only the errors
IEnumerable<Order> found = results.Successes();
IEnumerable<IError> failures = results.Failures();

// Map each element; fail fast on the first error
Result<IReadOnlyList<OrderDto>> dtos = orderIds.Traverse(id => FindOrder(id).Map(o => o.ToDto()));
```

`CombineAll`, `Partition`, `Successes` and `Failures` throw `ResultException` on a `default` result. The fail-fast `Combine` and `Traverse` throw on one they reach, and stop at the first failure, so a `default` after it goes unread. None of them reads a `default` as a success.

## Handle exceptions at a boundary

Infrastructure throws: a database, an HTTP client, a file system. Turn its exceptions into results where your code calls it, and nowhere else. Catch only the infrastructure's exceptions. Let a bug, an exhausted resource and the caller's cancellation propagate: a handler cannot recover from them.

### Declare technical errors with generic messages

A technical error carries a generic, client-safe message. The exception itself goes to the log.

```csharp
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
```

```csharp
using Microsoft.Extensions.Logging;

internal static partial class InfrastructureLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Database call failed")]
    public static partial void DatabaseCallFailed(this ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Remote service call failed")]
    public static partial void RemoteCallFailed(this ILogger logger, Exception exception);
}
```

### Catch infrastructure exceptions where the call takes a token

For a call that takes the caller's `CancellationToken`, catch the infrastructure's exception type, filtered so that a cancellation still propagates:

```csharp
using System.Data.Common;

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

An `HttpClient` timeout is a `TaskCanceledException` whose `InnerException` is a `TimeoutException`. Catch `OperationCanceledException` only when `ct.IsCancellationRequested` is `false`, and never `TaskCanceledException` alone.

### Wrap a call without a token with Try

For a call that takes no `CancellationToken`, such as a third-party client without one, use `Try` or `TryAsync` with a mapper. They catch every exception, a programming error included, and hand it to the mapper. The mapper logs it and returns an error with a generic message.

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

The mapper receives every exception, `OperationCanceledException` included, and decides. That is why `Try` and `TryAsync` suit only calls that take no token. `Result.Try` and `Result.TryAsync` do the same for a delegate that returns a `Result`.

A mapper that returns `null` throws `ArgumentNullException`, whose `InnerException` is the caught exception. A delegate that returns `null` throws `ArgumentNullException` to the caller: a success needs a value.

### Use Try without a mapper

Without a mapper, `Try` and `TryAsync` let every `OperationCanceledException` propagate, an `HttpClient` timeout included. They turn any other exception into an `ExceptionError`: the code `EXCEPTION`, the category `Technical`, the generic message `An unexpected error occurred.`, and no metadata.

`ExceptionError.Exception` keeps the exception in process, for logging. It is not serialized, and `ToString` does not print it.

## Return results over HTTP

`MicroKit.Result.AspNetCore` turns a result into a typed ASP.NET Core response. A success answers its status with the value. A failure answers RFC 9457 problem details, with the status of the error's category.

### Register problem details

```csharp
// Problem details go through IProblemDetailsService: CustomizeProblemDetails and the traceId apply
builder.Services.AddProblemDetails();
```

Without it, the response is the same problem details, written as JSON without those customizations.

### Map results to endpoints

```csharp
// ct is the request's token, cancelled when the client goes away
app.MapGet("/users/{id}", async (Guid id, IUserService svc, CancellationToken ct) =>
    (await svc.GetUserAsync(id, ct)).ToHttpResult());

// A command: 204 No Content on success
app.MapDelete("/users/{id}", async (Guid id, IUserService svc, CancellationToken ct) =>
    (await svc.DeleteUserAsync(id, ct)).ToHttpResult());

// 201 Created with the location of the new resource; the problem responses declared for OpenAPI
app.MapPost("/users", async (CreateUser command, IUserService svc, CancellationToken ct) =>
        (await svc.CreateUserAsync(command, ct)).ToCreatedHttpResult(user => $"/users/{user.Id}"))
    .ProducesProblem(StatusCodes.Status409Conflict)
    .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

// 202 Accepted with the location to poll
app.MapPost("/exports", async (StartExport command, IExportService exports, CancellationToken ct) =>
        (await exports.StartAsync(command, ct)).ToAcceptedHttpResult(job => $"/exports/{job.Id}"))
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
```

| Method | Success |
|---|---|
| `Result<T>.ToHttpResult()` | 200 OK with the value |
| `Result.ToHttpResult()` | 204 No Content |
| `Result<T>.ToCreatedHttpResult(location)` | 201 Created with the value and its location |
| `Result<T>.ToAcceptedHttpResult(location)` | 202 Accepted with the value and the location to poll |
| `Result.ToAcceptedHttpResult(location)` | 202 Accepted, the location optional |

Each method returns `TypedResults`, such as `Results<Ok<T>, ProblemHttpResult>`, so OpenAPI sees the success type. A `ProblemHttpResult` has no fixed status. Declare an endpoint's problem responses with `ProducesProblem` or `ProducesValidationProblem`.

A lambda that returns both `ToHttpResult()` and another typed result, such as `TypedResults.BadRequest()`, needs a declared return type. Declare it, or nest the union: `Results<BadRequest, Results<Ok<T>, ProblemHttpResult>>`.

### Status codes by category

`ResultProblemDetailsFactory.ToStatusCode` owns the mapping:

| Category | Status |
|---|---|
| `NotFound` | 404 Not Found |
| `Validation` | 422 Unprocessable Entity |
| `BusinessRule` | 422 Unprocessable Entity |
| `Unauthorized` | 401 Unauthorized |
| `Forbidden` | 403 Forbidden |
| `Conflict` | 409 Conflict |
| `TooManyRequests` | 429 Too Many Requests |
| `PreconditionFailed` | 412 Precondition Failed |
| `Cancelled` | 499 Client Closed Request |
| `Technical`, and any undefined value | 500 Internal Server Error |
| `NotSupported` | 501 Not Implemented |
| `External` | 502 Bad Gateway |
| `Unavailable` | 503 Service Unavailable |
| `Timeout` | 504 Gateway Timeout |

`Timeout` answers 504, not 408: the operation, typically a call to a dependency, did not complete in time, and 408 would blame the client's request.

### Read a problem details response

`detail` is the error's `Message`, which an error keeps safe for a client. `errorCode` is its `Code`. `Metadata` is never written. `type` is the section ASP.NET Core knows for the status (RFC 9110, and RFC 4918 for 422); 429 and 499 have none.

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Not Found",
  "status": 404,
  "detail": "User 0b5f8c1e-2d7a-4a8e-9c3b-6f1d2e4a5b6c not found",
  "errorCode": "USER.NOT_FOUND",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00"
}
```

A validation error, alone or in an `ErrorCollection`, answers an `HttpValidationProblemDetails`. Its `errors` groups the messages by `PropertyName`, so a front end can attach them to form fields. For an `ErrorCollection`, `errorCodes` lists the code of each error, nested collections flattened.

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

Only a `Validation` or `BusinessRule` response carries field errors. An `ErrorCollection` that mixes a validation error with a `Technical` one answers 500, with an `Unauthorized` one 401, with a `NotFound` one 404, each without `errors`: an unauthenticated caller receives no validation detail.

### Use the factory directly

```csharp
// Outside Minimal APIs, for an MVC action or a custom writer
ProblemDetails problem = ResultProblemDetailsFactory.CreateProblemDetails(error);
int status = ResultProblemDetailsFactory.ToStatusCode(error.Category);
```

## Serialize results to JSON

The converters live in `MicroKit.Result`, in the `MicroKit.Result.Serialization` namespace. They write and read a result with `System.Text.Json`.

### Register the converters

`ResultJsonConverterFactory` converts `Result<T>` and `Result`. Register it once per serializer options.

```csharp
// Minimal APIs
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new ResultJsonConverterFactory()));

// MVC controllers, which do not read the Minimal API options
builder.Services.AddControllers().AddJsonOptions(o =>
    o.JsonSerializerOptions.Converters.Add(new ResultJsonConverterFactory()));

// An in-process store such as a cache, never a response: also write Metadata
var cacheOptions = new JsonSerializerOptions();
cacheOptions.Converters.Add(new ResultJsonConverterFactory(includeMetadata: true));
```

By default the converters never write `Metadata`. Create the factory with `includeMetadata: true` only for a payload that stays in process, such as a cache entry. Metadata keys survive a round trip; values come back as JSON primitives. An integer comes back as a `long`, a `Guid` or a `DateTime` as a `string`, another object as its invariant string.

The factory uses reflection to build `ResultJsonConverter<T>`, so it is not trim or NativeAOT safe.

For an HTTP response, prefer `ToHttpResult()`, which answers problem details on failure.

### Round-trip a result

```csharp
Result<Order> failed = Error.NotFound(ErrorCode.From("ORDER.NOT_FOUND"), $"Order {orderId} does not exist.");
string json = JsonSerializer.Serialize(failed, options);
Result<Order> restored = JsonSerializer.Deserialize<Result<Order>>(json, options);

// The non-generic Result carries its own converter: no registration needed
string command = JsonSerializer.Serialize(Result.Success()); // {"isSuccess":true}
```

The non-generic `Result` carries `ResultJsonConverter` as its `[JsonConverter]`. It uses no reflection and never writes `Metadata`, whether or not the factory is registered.

### Wire format

A success writes `isSuccess` and `value`:

```json
{"isSuccess":true,"value":{"id":"0b5f8c1e-2d7a-4a8e-9c3b-6f1d2e4a5b6c"}}
```

A failure writes `isSuccess` and `error`. The error object carries `code`, `message`, `category` and `severity`, `propertyName` for a validation error, and `errors` for an `ErrorCollection`:

```json
{
  "isSuccess": false,
  "error": {
    "code": "MULTI.ERROR",
    "message": "Email is required.; Password must be at least 8 characters.",
    "category": "Validation",
    "severity": "Error",
    "errors": [
      { "code": "VALIDATION.EMAIL", "message": "Email is required.", "category": "Validation", "severity": "Warning", "propertyName": "Email" },
      { "code": "VALIDATION.PASSWORD", "message": "Password must be at least 8 characters.", "category": "Validation", "severity": "Warning", "propertyName": "Password" }
    ]
  }
}
```

Reading rebuilds an `ErrorCollection`, a `DeserializedValidationError`, which keeps `PropertyName`, or a `DeserializedError`. An unknown category reads as `Technical`, an unknown severity as `Error`.

### Invalid payloads

A payload that does not describe a valid result throws `JsonException`; it is never read as a success. That covers a success without `value` or with `error`, a failure without `error` or with `value`, a `null` value, a missing or duplicate `isSuccess`, and an empty `code`. Writing a `default` result throws `JsonException` too. [Troubleshooting](#jsonexception-when-reading-or-writing-a-result) lists every message.

## Catch mistakes with the analyzers

`MicroKit.Result.Analyzers` reports two mistakes the compiler accepts. Both are warnings, so they fail a build that treats warnings as errors.

### MKR001: a result is discarded

MKR001 reports a `Result` or `Result<T>` that a statement returns and then drops. Its failure is never handled. The message reads `The Result<Order> returned here is discarded: its failure is never handled`.

It reports an expression statement whose value is a result: a call such as `repository.Save(order);`, an `await`, or a conditional access such as `repository?.Save(order);`. It also reports a `Task` or a `ValueTask` of a result dropped where CS4014 does not: outside an async method or lambda, or through a conditional access or a property inside one.

It does not report:

- An assignment, an explicit discard `_ = …` included.
- `Tap`, `TapError` and `Finally` of MicroKit.Result, and their `Async` forms awaited, with or without `ConfigureAwait`, alone or chained, on a result already held in a local, a parameter, a field, a property or an array element. The side effect is the point, and the result is still held. A method of yours with one of these names is reported.

To fix it, handle the result. Assign it and check the failure rail, return it, or await it:

```csharp
public Result Place(Order order)
{
    Result saved = repository.Save(order);              // held in a local: not reported
    saved.TapError(e => metrics.RecordFailure(e.Code)); // a side effect on a held result: not reported
    if (saved.IsFailure)
        return saved;

    return repository.Publish(order);                   // returned: the caller handles it
}
```

### MKR002: an error keeps the default category

MKR002 reports a concrete type deriving from `Error` that never overrides `Category`. It is then a `Technical` error, an internal fault, which `MicroKit.Result.AspNetCore` answers with 500. The message reads `'UserNotFoundError' does not override Category: it is a Technical error, an internal fault (HTTP 500 in MicroKit.Result.AspNetCore)`.

It does not report:

- A type that overrides `Category`, or whose base type does, anywhere between the type and `Error`. `CategorizedError`, `ValidationError` and `ExceptionError` override it.
- An abstract type. Its concrete subtypes are checked instead.

To fix it, override `Category` with the kind of failure, or build the error with a factory of `Error`. When the error really is an internal fault, override `Category` with `Technical` explicitly.

```csharp
// A base type that overrides Category settles it for its subtypes
public abstract record PaymentError(ErrorCode Code, string Message) : Error(Code, Message)
{
    public override ErrorCategory Category => ErrorCategory.BusinessRule;
}

public sealed record CardExpiredError() : PaymentError(ErrorCode.From("PAYMENT.CARD.EXPIRED"), "The card has expired.");

// An internal fault says so explicitly
public sealed record LedgerCorruptedError() : Error(ErrorCode.From("LEDGER.CORRUPTED"), "An internal error occurred.")
{
    public override ErrorCategory Category => ErrorCategory.Technical;
}
```

### Change the severity of a diagnostic

Both diagnostics are warnings by default. An `.editorconfig` entry raises or lowers them:

```ini
[*.cs]
dotnet_diagnostic.MKR001.severity = error
dotnet_diagnostic.MKR002.severity = error
```

### What the analyzers do not check

Reading `.Value` without checking the rail first has no analyzer: a check made in a helper method is invisible to an analysis limited to one method. Use `Match` or `TryGetValue`, or read `Value` right after testing `IsSuccess`.

Blocking on a `ValueTask`, with `.Result` or `.GetAwaiter().GetResult()` before it completes, is reported by CA2012 of the .NET code analyzers, not by MicroKit.Result.Analyzers.

## Test code that returns results

Test both rails of each operation, each in its own test. Assert the rail first, then read `Error` or `Value`. Never read `Value` before the rail is known: on a failure it throws, and the test reports the wrong problem.

The block below uses `Check`, a stand-in for your framework's assertion:

```csharp
internal static class OrderServiceTests
{
    public static void Place_UnknownProduct_FailsWithNotFound()
    {
        Result<Order> result = OrderService.Place(UnknownProductId, quantity: 1);

        Check(result.IsFailure, "the result is a failure");             // the rail first
        Check(result.Error is ProductNotFoundError, "a typed error");   // then the error
        Check(result.Error.Category == ErrorCategory.NotFound, "a NotFound category");
    }

    public static void Place_KnownProduct_SucceedsWithTheQuantity()
    {
        Result<Order> result = OrderService.Place(KnownProductId, quantity: 2);

        Check(result.TryGetValue(out Order? order), "the result is a success");
        Check(order.Quantity == 2, "the order keeps the quantity");
    }

    public static void Default_IsNeitherSuccessNorFailure()
    {
        Result<Order> uninitialized = default;

        Check(!uninitialized.IsSuccess && !uninitialized.IsFailure, "neither rail");
    }

    // Your framework's assertion: Assert.True, ShouldBeTrue…
    private static void Check([DoesNotReturnIf(false)] bool condition, string expectation)
    {
        if (!condition)
            throw new InvalidOperationException($"Expected {expectation}.");
    }
}
```

MicroKit.Result's own tests use xUnit and Shouldly, where the first test reads `result.IsFailure.ShouldBeTrue()` then `result.Error.ShouldBeOfType<ProductNotFoundError>()`.

`Result` and `Result<T>` compare by rail, value and error, and error records compare by value. An equality assertion against an expected result works too.

A test assigns every result it gets and checks it. A result left unchecked hides the failure the test exists to find, and [MKR001](#mkr001-a-result-is-discarded) reports it.

## Troubleshooting

Each entry quotes the message as the library or the compiler writes it.

### ResultException: Cannot access Value on a failed result

- Symptom: `ResultException: Cannot access Value on a failed result. Error: [USER.NOT_FOUND] User … not found`
- Cause: the code read `Value` of a failure, directly or through `GetValueOrThrow()` without a factory. `ResultException.Errors` holds the error.
- Fix: check the rail before reading the value. Use `Match`, `TryGetValue`, or an early return on `IsFailure`. See [Read the value](#read-the-value).

### ResultException: Cannot access Error on a successful result

- Symptom: `ResultException: Cannot access Error on a successful result.`
- Cause: the code read `Error` of a success, often after testing `!IsSuccess` on a path that a success also reaches.
- Fix: read `Error` only after `IsFailure` is `true`, or in the `onFailure` function of `Match`.

### ResultException: Result was not initialized

- Symptom: `ResultException: Result was not initialized. Use Result.Success() or Result.Failure() factory methods.`
- Cause: the result is `default`: `new Result<T>()`, `default`, or a field or array element never assigned. `Value` and `Error` throw on it, and so does every operation that reads them, such as `Map`, `Bind`, `Ensure`, `Match`, `Combine`, `Partition`, `Traverse` and `ToHttpResult`. `IsSuccess` and `IsFailure` are both `false`, and `TryGetValue` returns `false`.
- Fix: build every result with a factory (`Success`, `Failure`, an `Error` factory) or an implicit conversion, and assign every field that holds one.

### ArgumentNullException: A successful result needs a value

- Symptom: `ArgumentNullException: A successful result needs a value: null means absence, which is a failure (for instance Error.NotFound). A mapper or delegate that can return null belongs in Bind, returning that failure (ADR-RESULT-001 D6). (Parameter 'value')`
- Cause: a success was built from `null`: `Success(null)`, the conversion from a null value, a `Map`, `MapAsync`, `Select` or `SelectMany` delegate that returned `null`, or a `Try` / `TryAsync` delegate that returned `null`.
- Fix: return a failure for an absent value. Use `Bind` and return an error such as `Error.NotFound(code, message)` when the value is `null`.

### ArgumentNullException: The exception mapper returned null

- Symptom: `ArgumentNullException: The exception mapper returned null; it must return an error. The caught exception is the inner exception.`
- Cause: the mapper given to `Try` or `TryAsync` returned `null`.
- Fix: return an error from every path of the mapper, a generic `TechnicalError` for the exceptions you do not recognize. The caught exception is in `InnerException`.

### ArgumentException: A result cannot be the value of another result

- Symptom: `ArgumentException: A result cannot be the value of a Result<T>: a failure would become a success. Chain it with Bind instead. (Parameter 'value')`
- Cause: the value of a success is itself a result: a `Map` or `Select` delegate that returns a result, or, when `T` is `object` or `ValueType`, `Result<object> r = otherResult;` or `Result.Success<object>(otherResult)`.
- Fix: chain the inner result with `Bind`.

### InvalidOperationException: The validation has no error

- Symptom: `InvalidOperationException: The validation has no error: ToResult<T>() returns a failure only. Use ToResult<T>(value) instead.`
- Cause: `ValidationResult.ToResult<T>()` was called on a valid `ValidationResult`.
- Fix: call it only after `IsValid` is `false`, or call `ToResult<T>(value)`, which returns the success or the failure.

### JsonException when reading or writing a result

- Symptom: `JsonException` from a result converter, with one of the messages below.
- Cause: the payload does not describe a valid result, or the result written is `default`. The converters never read an invalid payload as a success.
- Fix: correct the producer of the payload, or build the result with a factory before writing it.

| Message | Cause |
|---|---|
| `Cannot serialize an uninitialized (default) Result<T>: it is neither a success nor a failure.` | Writing a `default` `Result<T>`. |
| `Cannot serialize an uninitialized (default) Result: it is neither a success nor a failure.` | Writing a `default` `Result`. |
| `Cannot serialize an error of type <Type> with an uninitialized code.` | Writing an error whose `Code` is `default`. Build codes with `ErrorCode.From`. |
| `Expected StartObject token.` | The result is not a JSON object. |
| `Expected PropertyName token.` | The result object is malformed. |
| `Missing 'isSuccess' property.` | The result has no `isSuccess`. |
| `Duplicate 'isSuccess' property.`, `Duplicate 'value' property.`, `Duplicate 'error' property.` | A property appears twice. |
| `A successful result requires a 'value' property.` | A `Result<T>` success without `value`. |
| `The 'value' of a successful result must not be null or a result.` | `"value": null`, or a value that is itself a result. |
| `A successful result cannot carry an 'error' property.` | A success with an `error`. |
| `A failed result requires an 'error' property.` | A failure without `error`. |
| `A failed result cannot carry a 'value' property.` | A `Result<T>` failure with a `value`. |
| `A non-generic result cannot carry a 'value' property.` | A `Result` payload with a `value`. |
| `Expected an error object.` | `error` is not an object. |
| `Expected a property name in the error object.` | The error object is malformed. |
| `Duplicate '<name>' property in the error object.` | A property of the error appears twice. |
| `The error 'code' must not be empty.` | `code` is empty or whitespace. |
| `Expected an array of errors.` | `errors` is not an array. |
| `An error collection cannot be empty.` | `errors` is an empty array. |
| `Expected a metadata object.` | `metadata` is not an object. |

### CS0457: Ambiguous user defined conversions

- Symptom: `error CS0457: Ambiguous user defined conversions 'Result<Error>.implicit operator Result<Error>(Error)' and 'Result<Error>.implicit operator Result<Error>(Error)' when converting from 'Error' to 'Result<Error>'`
- Cause: an error converts to `Result<Error>` or `Result<ErrorCollection>`, where the conversion from the value and the conversion from the error both apply.
- Fix: say what you mean: `Result.Success(error)` for a result whose value is an error, `Result.Failure<Error>(error)` for a failure.

### CS0037: Cannot convert null to a result

- Symptom: `error CS0037: Cannot convert null to 'Result<string>' because it is a non-nullable value type`
- Cause: `Result<string> r = null;`, `return null;` in a method that returns a result, or `result.Equals(null)`. A result is a struct and is never `null`.
- Fix: return a failure, such as `Error.NotFound(code, message)`. Write `result.Equals((object?)null)` if you really compare to `null`.

### CS8866: a positional parameter named after a category

- Symptom: `error CS8866: Record member 'MicroKit.Result.Error.Timeout' must be a readable instance property or field of type 'System.TimeSpan' to match positional parameter 'Timeout'.`
- Cause: a record deriving from `Error` has a positional parameter named after a factory of `Error`, such as `Timeout`, `Conflict`, `External` or `Validation`. The factory is an inherited member of that name.
- Fix: rename the parameter, for example `TimeoutAfter`. A property or method of such a name declared in an `Error` subtype hides the factory and raises warning CS0108, an error when warnings are errors: rename it, or declare it `new`.

### CS0121: the call is ambiguous between Tap overloads

- Symptom: `error CS0121: The call is ambiguous between the following methods or properties: 'MicroKit.Result.AsyncResultExtensions.Tap<T>(MicroKit.Result.Result<T>, System.Func<T, System.Threading.Tasks.Task>)' and 'MicroKit.Result.ResultExtensions.Tap<T>(MicroKit.Result.Result<T>, System.Action<T>)'`
- Cause: a method group passed to `Tap`, `TapError` or `Finally` has an overload that returns `void` and another that returns a `Task`, so it matches both the `Action` and the `Task` overload.
- Fix: pass a lambda, `v => Log(v)`, or cast the method group to the delegate you mean, `(Action<int>)Log`.

### CS1662 in a Bind lambda with a conditional

- Symptom: `error CS1662: Cannot convert lambda expression to intended delegate type because some of the return types in the block are not implicitly convertible to the delegate return type`, with `CS0029` on one branch.
- Cause: `Bind(u => u.IsActive ? u : new UserInactiveError(u.Id))`. The conditional has no type, so the lambda's return type cannot be inferred.
- Fix: give one branch the result type, `u.IsActive ? Result.Success(u) : new UserInactiveError(u.Id)`, or use `Ensure`.

### CA2012 on Tap, TapError or Finally

- Symptom: `CA2012: ValueTasks should be used correctly` on a `Tap`, `TapError` or `Finally` call.
- Cause: a lambda that is not `async` returns a `ValueTask`. It binds to the `Action` overload, which discards the `ValueTask`. Or a `Task`-returning lambda in a synchronous method: the call returns a `ValueTask` that nothing awaits.
- Fix: write the lambda `async x => await port(x)` and await the call.

### MKR001: The result returned here is discarded

- Symptom: `warning MKR001: The Result<Order> returned here is discarded: its failure is never handled`
- Cause and fix: see [MKR001: a result is discarded](#mkr001-a-result-is-discarded).

### MKR002: an error does not override Category

- Symptom: `warning MKR002: 'UserNotFoundError' does not override Category: it is a Technical error, an internal fault (HTTP 500 in MicroKit.Result.AspNetCore)`
- Cause and fix: see [MKR002: an error keeps the default category](#mkr002-an-error-keeps-the-default-category).
