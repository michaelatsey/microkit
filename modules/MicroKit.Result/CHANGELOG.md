# Changelog — MicroKit.Result

All notable changes to this package are documented here.
Format: [Keep a Changelog](https://keepachangelog.com/en/1.0.0/) — [Semantic Versioning](https://semver.org/).

---

## [Unreleased]

Hardening before stable (#184). Breaking changes ship outright on `preview.*` (ADR-GLOBAL-002 D10).

### Added
- Unit tests of the core (both rails, `default`, async on every surface), FsCheck property tests of the
  `Map` and `Bind` laws, and HTTP integration tests of the problem-details contract (#184 Phases 1–2).
- `ExceptionError` is public: the error `Try` / `TryAsync` build without a mapper. Its `Exception`
  property keeps the exception in process for logging and is not serialized.
- `Tap`, `TapError` and `Finally` overloads taking a `Task`-returning delegate, on `Result<T>`, `Result`,
  `Task<Result<T>>` and `ValueTask<Result<T>>`: an async lambda now binds to an overload that awaits it.
- `IsAotCompatible` on both packages: the trim, single-file and AOT analyzers run, with no warning.
- `MicroKit.Result.Analyzers` (#184 Phase 6), a build-time package (`DevelopmentDependency`): `MKR001`
  reports a `Result` or `Result<T>` returned and then discarded (a call, an `await`, a conditional access), and
  a `Task` of a result discarded outside an async context; side effects (`Tap`, `TapError`, `Finally`) on a
  held result are not reported; `MKR002` reports a concrete `Error` subtype that keeps the default
  `Technical` category (ADR-RESULT-001 D5). Reading `.Value` without a prior check has no analyzer: a check in a
  helper is invisible to an analysis limited to one method.
- ADR-RESULT-001, the error model (#184 Phase 3), implemented by Phase 4:
  - `CategorizedError` and one factory per category on `Error` (`Error.NotFound(code, message)`…).
  - Implicit conversions from `Error` and `ErrorCollection` to `Result<T>` and to `Result` (a failure).
  - The non-generic `Result` gets `Map`, `Bind` (to `Result` and to `Result<T>`), `Ensure`, `MapError`,
    `Compensate`, and every async operation; `Result<T>.Bind(Func<T, Result>)` and its `BindAsync`.
  - Every `XxxAsync` gains a form whose delegate takes the `CancellationToken` and returns a `ValueTask`,
    with `CancellationToken ct = default` last, on `Result<T>` and `Result`.
  - `ValidationResult.ToResult<T>()`: a failure-only result (throws on a valid validation).

### Changed (breaking)
- `Try` / `TryAsync` without a mapper rethrow `OperationCanceledException` instead of returning a failure.
- The error they build has the code `EXCEPTION` and the message `An unexpected error occurred.`, not
  the exception's message, and no `exceptionType` metadata.
- `Result<T>.Try` / `TryAsync` with a mapper that returns `null` throw `ArgumentNullException`.
- The implicit conversion from `T` to `Result<T>` throws `ArgumentException` when the value is itself a
  `Result` or a `Result<T>` (possible when `T` is `object`).
- `Combine`, `CombineAll`, `Successes` and `Failures` throw `ResultException` on a `default` result.
- `ErrorCollection.WithCategory`, `WithSeverity` and `WithCode` return `IReadOnlyList<IError>`, and
  `OfType<TError>` returns `IReadOnlyList<TError>`: a filter that matches nothing returns an empty list,
  never an empty `ErrorCollection`.
- `ErrorCollection.Category` is the category its errors share; otherwise the one of highest precedence, so the order of the errors never matters.
  Precedence, highest first: faults of the system (`Technical`, `External`, `Unavailable`, `Timeout`, `NotSupported`, undefined values), then `Unauthorized` and `Forbidden`, then `NotFound`, `Conflict`, `PreconditionFailed` and `TooManyRequests`, then `Cancelled`, then `Validation` and `BusinessRule`;
  inside a group, the category declared first in `ErrorCategory` wins.
- `ToHttpResult()` writes grouped field errors only when the category is `Validation` or `BusinessRule`: a
  collection that also holds an access, resource or server error answers that error, without `errors`, and
  its `detail` and `errorCodes` name only the errors of that category.
- `ResultJsonConverter<T>.Read` throws `JsonException` for a payload that does not describe a valid
  result: a success without `value` or with `error`, a failure without `error` or with `value`, a
  duplicate `isSuccess`, an error without a non-empty `code` (missing, `null` or empty), a duplicate metadata
  key.
- The async extensions and `TryAsync` validate their arguments, the source task included, when called,
  before anything is awaited.
- `ToHttpResult()` / `ResultProblemDetailsFactory.CreateProblemDetails`: a validation error, alone or in
  an `ErrorCollection`, answers an `HttpValidationProblemDetails` whose `errors` groups the messages by
  `PropertyName`; a 5xx response carries no field errors. The previous `errors` list of `{ code, message }`
  is replaced by `errorCodes`, the code of each error of an `ErrorCollection`, nested ones flattened (on a
  non-validation response, only those of its category).
- `Result<T>.Success` (and `Result.Success<T>`) throw `ArgumentException` when the value is itself a result,
  as the implicit conversion does.
- `ExceptionError.ToString()` prints its code, message and category, never the exception.
- A mapper that returns `null` throws `ArgumentNullException` whose `InnerException` is the caught exception.

### Migration notes
- `Tap`, `TapError` and `Finally` given a `Task`-returning lambda or method group, async or not, now return
  a `ValueTask<Result<T>>` (or `ValueTask<Result>`) that must be awaited. A statement such as
  `result.Tap(async x => ...);` in a synchronous method now drops that `ValueTask` (CA2012 reports it)
  instead of running an `async void`.
- `Tap(null!)` now binds to the awaited `Func<T, Task>` overload. A method group with both a `void` and a
  `Task`-returning overload is ambiguous (CS0121): pick one with a lambda or a cast.
- A lambda that only throws (`_ => throw new X()`) binds to the awaited overload: the exception surfaces
  at the await.
- `result.Equals(null)` no longer compiles (the `null` literal binds to `Equals(Result<T>)` through the
  conversion from `Error`): write `result.Equals((object?)null)`.
- `ResultCombineExtensions.Combine(...)` becomes `Result.Combine(...)`.
- `ToHttpResult()` returns `Results<…>`: assigning it to `IResult` still works, but a lambda without a
  declared return type that also returns another typed result (`TypedResults.BadRequest()`), or a conditional
  mixing two `ToHttpResult()` types, no longer compiles. Declare the lambda's return type, or nest the union
  (`Results<BadRequest, Results<Ok<T>, ProblemHttpResult>>`). A test that casts the value to
  `IStatusCodeHttpResult` or tests `is ProblemHttpResult` reads `.Result` first.
- With `ResultJsonView.Full`, metadata values come back as JSON primitives: `TryGetMetadata<int>` after a
  round trip reads a `long`, `TryGetMetadata<Guid>` a `string`.
- `Result<string> r = null;` and `return null;` in a method returning a result no longer compile (CS0037):
  the conversions from `Error` and `ErrorCollection` both accept `null`. They used to give a null success.
- A record deriving from `Error` with a positional parameter named after a category (`Timeout`,
  `Conflict`, `External`, `Validation`…) no longer compiles (CS8866: the factory `Error.Timeout(…)` is an
  inherited member of that name): rename the parameter, for example `TimeoutAfter`. A property or method of
  such a name declared in an `Error` subtype hides the inherited factory (CS0108, an error with
  `TreatWarningsAsErrors`): rename it, or declare it `new`.
- `Bind(u => u.IsActive ? u : new UserInactiveError(u.Id))` does not compile (the conditional has no type):
  write `u.IsActive ? Result.Success(u) : new UserInactiveError(u.Id)`, or use `Ensure`.

### Changed (breaking, Phase 4)
- `Result.Success<T>` / `Result<T>.Success` and the implicit conversion from `T` throw
  `ArgumentNullException` on `null`; `Try` / `TryAsync` whose delegate returns `null` throw it to the caller;
  the JSON converter reads `"value": null` as an invalid payload (ADR-RESULT-001 D6).
- `Combine` and `CombineAll` are static members of `Result` (`Result.Combine(...)`).
- An `Error` converted to `Result<object>`, `Result<IError>` or another unrelated `T`, and an
  `ErrorCollection` converted to `Result<IReadOnlyList<IError>>`, are now failures, not successes;
  `Result<Error> r = error;` and `Result<ErrorCollection> r = errors;` are ambiguous (CS0457).
- `Map`, `MapAsync`, `Select` and `SelectMany` with a mapper that returns `null` throw
  `ArgumentNullException`: a success needs a value. Return a failure from `Bind` instead.
- `ErrorCollection.FirstOrDefault` is typed `IError` (never `null`).
- The core's XML docs no longer cite HTTP statuses; the mapping is documented on
  `ResultProblemDetailsFactory.ToStatusCode`.

### Changed (breaking, Phase 5 — HTTP contract)
- `ToHttpResult()` returns `Results<Ok<T>, ProblemHttpResult>` / `Results<NoContent, ProblemHttpResult>`
  (`TypedResults`) instead of `IResult`: OpenAPI sees the success type. New `ToCreatedHttpResult(location)`
  (201) and `ToAcceptedHttpResult(location)` (202).
- `ErrorCategory.Timeout` answers 504 Gateway Timeout instead of 408, and counts as a server fault in an
  `ErrorCollection`. `Cancelled` uses `StatusCodes.Status499ClientClosedRequest`.
- `ProblemDetails.Type` is the section ASP.NET Core knows for the status (RFC 9110; RFC 4918 for 422), absent
  for 429 and 499, instead of a link to httpstatuses.io.
- JSON: the error object adds `severity`, `propertyName` and the `errors` of a collection; `metadata` only
  with `new ResultJsonConverterFactory(ResultJsonView.Full)`. Reading returns an `ErrorCollection`, a
  public `DeserializedValidationError` or `DeserializedError`. Writing a `default` result throws
  `JsonException`.
- The non-generic `Result` has a converter, `ResultJsonConverter`, carried by the type: returned as is, it
  serializes as `{"isSuccess":…}` without `metadata`. The factory converts `Result` too.

### Changed (breaking, #195 — what #184 left open)
- `ResultProblemDetailsFactory.CreateProblemDetails` / `ToHttpResult()`: from status 500 on (`Technical`,
  `External`, `Unavailable`, `NotSupported`, `Timeout`, undefined values), `detail` is the generic
  `"The server could not complete the request."`, never the error's `Message`, collections included;
  `errorCode` and `errorCodes` are kept. 4xx responses keep the message.
- `Result.Try` / `TryAsync` throw `ResultException` when the delegate returns a `default` result, outside their
  `catch` (the mapper never sees it). `MapError`, `Compensate` and every async form throw `ResultException` on a
  `default` result without calling their delegate, instead of returning it; the async forms when awaited.
- `GetValueOrThrow(factory)` throws `InvalidOperationException` when the factory returns `null`, instead of a
  bare `NullReferenceException`.
- `Select`, `SelectMany` and `Where` report their own parameter names (`selector`, `errorOnFalse`) in
  `ArgumentNullException.ParamName`.
- `ErrorCollection.Severity` is the highest severity of its errors: a collection of warnings is a `Warning`,
  no longer an `Error`.
- `ErrorMetadata.Create` returns an `ImmutableDictionary` (the return type stays
  `IReadOnlyDictionary<string, object?>`): a cast no longer lets a caller mutate an error. Its keys no longer
  enumerate in insertion order.
- `ValidationError`: `with { PropertyName = … }` recomputes `Code` (`VALIDATION.<NAME>`) and validates the name.
  `OutOfRange` formats its bounds with the invariant culture.
- `DeserializedError` and `DeserializedValidationError` compare `Metadata` by content (a `JsonElement` by its
  JSON), not by reference.
- `Result<T>` carries `ResultEnvelopeJsonConverterFactory` as its `[JsonConverter]`, reflection-free: with no
  converter registered, a `Result<T>` (an endpoint's return value included) writes the documented envelope
  without `metadata` instead of failing with a 500; reading one throws `JsonException`, as any unreadable payload,
  naming `ResultJsonConverterFactory` and `ResultJsonConverter<T>`, instead of silently returning a `default`
  result.
  A registered converter still takes precedence.
- The JSON converters, registered or carried by `Result` and `Result<T>`, write
  `"The server could not complete the request."` as the `message` of a fault of the system (`Technical`,
  `External`, `Unavailable`, `Timeout`, `NotSupported`, undefined values), a collection's errors included: their
  output may reach a client. A collection whose category is not `Validation` or `BusinessRule` names, as
  `ToHttpResult` does, only its errors of that category, nested collections flattened, and no `propertyName`.
  Only a converter created with `ResultJsonView.Full`, for in-process storage (a cache, an idempotency store),
  writes every message and every error; a default round trip reads the client view back.
- The converters' `bool includeMetadata` constructor parameter is replaced by `ResultJsonView view`, on
  `ResultJsonConverterFactory`, `ResultJsonConverter<T>` and `ResultJsonConverter`: the switch selects a view,
  `ResultJsonView.Client` (the default, what a client may read) or `ResultJsonView.Full` (in-process storage:
  every error, message and `Metadata`), not only whether metadata is written. `new ResultJsonConverterFactory(true)`
  becomes `new ResultJsonConverterFactory(ResultJsonView.Full)`; an undefined value throws
  `ArgumentOutOfRangeException`.
- `ResultJsonConverter<T>` loses `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`: it reads and writes the
  value through `options.GetTypeInfo(typeof(T))` and is NativeAOT safe, registered next to a source-generated
  context that declares `Result<T>` and `T`. Only `ResultJsonConverterFactory` keeps the annotations.
- MKR001 reports a side effect (`Tap`, `TapError`, `Finally`) on a computed property declared in source: it builds
  a new result at each read, even when its body only returns a field. A property from a referenced assembly
  (`KeyValuePair<TKey, TValue>.Value`, `Nullable<T>.Value`), an abstract, interface, virtual or overriding
  property, an auto-property and an indexer still count as held.
- `Error.Code` calls the new `protected virtual void ValidateCode(ErrorCode)` when set after construction (a `with`
  expression, an object initializer); the property itself stays non-virtual. `ValidationError` overrides
  `ValidateCode`: `with { Code = … }`, through `ValidationError` or through `Error`, throws `ArgumentException`
  unless the code is the one its `PropertyName` gives. Members of a `with` are set in order: set `PropertyName`
  alone.

### Added (#195)
- `ResultJsonView` (`MicroKit.Result.Serialization`): `Client`, the default, and `Full`, the view the result
  converters write.
- `ErrorCategoryExtensions`: `IsSystemFault()`, `DescribesInput()` and `SystemFaultMessage`, the one definition of
  a fault of the system, of the input categories and of the text a client reads instead of a fault's message, used
  by the JSON converters and by `MicroKit.Result.AspNetCore`.
- `ResultEnvelopeJsonConverterFactory` (`MicroKit.Result.Serialization`): the reflection-free converter the
  result types carry; infrastructure, not meant to be registered.
- `tests/MicroKit.Result.AotTests`: `System.Text.Json` reflection disabled, trim and AOT analyzers on; a
  `Result<OrderDto>` round-trips through a source-generated context only.

### Performance (Phase 7)
- Published BenchmarkDotNet figures in the README (machine and SDK stated), against ErrorOr 2.1.1 and
  FluentResults 4.0.0, and `ValueTask<Result<T>>` against `Task<Result<T>>`.
- `ErrorCollection` keeps its `Message` built by `string.Join` over LINQ: the best measured candidate gained
  18–19 %, under the 20 % the phase required to change the failure-rail path.

### Removed
- The internal, unused `ResultState` enum.
- `ResultCombineExtensions`: its members are on `Result`.

---

## [1.0.0-preview.2] — 2026-06-22

### Added
- `ArchitectureTests` — 44 real tests covering layer dependency rules, sealed/static type conventions,
  namespace conventions, and contract placement for both `MicroKit.Result` and `MicroKit.Result.AspNetCore`

### Changed
- `MicroKit.Result.Samples` marked `IsPackable=false` / `IsPublishable=false` — prevents the sample
  executable from being pushed to NuGet during releases

> No functional changes to the published packages.

---

## [1.0.0-preview.1] — 2026-05-28

First public pre-release of MicroKit.Result.

### Added
- `Result<T>` and `Result` (non-generic) types — Railway-Oriented Programming core
- `Unit` type for void-safe generic pipelines
- Error hierarchy: `IError`, `Error`, `ErrorCode`, `ErrorCategory`, `ErrorSeverity`
- `ErrorCollection` for aggregating multiple errors
- `ErrorMetadata` and `ErrorMetadataBuilder` for structured error context
- `ExceptionError` (internal) — wraps exceptions caught by `Try` / `TryAsync`
- `ResultException` for safe unwrap-or-throw scenarios
- Sync extensions: `Map`, `Bind`, `Tap`, `TapError`, `Ensure`, `Match`, `MapError`, `Compensate`, `Finally`
- `AsyncResultExtensions` — async counterparts of the nine operations of `Result<T>`, each returning a `ValueTask`: `MapAsync`, `BindAsync`… on `Result<T>` with a `Task`-returning delegate, and `Map`, `Bind`… on `Task<Result<T>>` and `ValueTask<Result<T>>` with a synchronous delegate
- `ResultCombineExtensions` — `Combine` (fail-fast) and `CombineAll` (collect-all)
- `ResultEnumerableExtensions` — `Partition`, `Successes`, `Failures`, `Traverse`
- `ResultLinqExtensions` — LINQ query syntax (`from x in result select ...`)
- `Try` / `TryAsync` on `Result` and `Result<T>`, mirrored by `ResultFactory` — exception boundary wrapping
- `ValidationError` and `ValidationResult` — structured validation support
- `ErrorExtensions` — `IsCategory`, `IsCritical`, `IsWarning`, `IsInformation`, `HasMetadata`, `TryGetMetadata`, `ToException`
- **MicroKit.Result.AspNetCore**: `ResultHttpExtensions`, `ResultProblemDetailsFactory`
  - Automatic `ErrorCategory` → HTTP status code mapping (404/422/401/403/409/429/500/503/502/501/408/499/412)
- **Serialization**: `ResultJsonConverter<T>` and `ResultJsonConverterFactory` — System.Text.Json converters for `Result<T>`, annotated `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`

### Fixed
- `[RequiresDynamicCode]` attribute added to `ResultJsonConverterFactory` for NativeAOT compliance
- `IsFailure` tag comparison corrected; removed incorrect `MemberNotNullWhen` attribute on `Result<T>`
- `ArgumentException.ThrowIfNullOrWhiteSpace` guard added to `ErrorCode.From()`
- Null guard added to `ValidationError` field code before `ToUpperInvariant()`
- `ErrorCollection.From()` now guards against empty input

### Changed
- Test assertion library migrated from FluentAssertions to Shouldly (MIT license compliance)
- `AsyncResultExtensions` reorganized into operation-based regions for maintainability

---

[1.0.0-preview.2]: https://github.com/michaelatsey/MicroKit/releases/tag/result-v1.0.0-preview.2
[1.0.0-preview.1]: https://github.com/michaelatsey/MicroKit/releases/tag/result-v1.0.0-preview.1
