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
- `ErrorCollection.Category` is the category its errors share; otherwise the first server-fault category
  (`Technical`, `Unavailable`, `External`, `NotSupported`); otherwise the first error's.
- `ResultJsonConverter<T>.Read` throws `JsonException` for a payload that does not describe a valid
  result: a success without `value` or with `error`, a failure without `error` or with `value`, a
  duplicate `isSuccess`, an empty `code`.
- The async extensions and `TryAsync` validate their arguments, the source task included, when called,
  before anything is awaited.
- `ToHttpResult()` / `ResultProblemDetailsFactory.CreateProblemDetails`: a validation error, alone or in
  an `ErrorCollection`, answers an `HttpValidationProblemDetails` whose `errors` groups the messages by
  `PropertyName`; a 5xx response carries no field errors. The previous `errors` list of `{ code, message }`
  is replaced by `errorCodes`, the code of each error of an `ErrorCollection`, nested ones flattened.
- `Result<T>.Success` (and `Result.Success<T>`) throw `ArgumentException` when the value is itself a result,
  as the implicit conversion does.
- `ExceptionError.ToString()` prints its code, message and category, never the exception.
- A mapper that returns `null` throws `ArgumentNullException` whose `InnerException` is the caught exception.

### Migration notes
- `Tap`, `TapError` and `Finally` given a `Task`-returning lambda or method group, async or not, now return
  a `ValueTask<Result<T>>` (or `ValueTask<Result>`) that must be awaited. A statement such as
  `result.Tap(async x => ...);` in a synchronous method now drops that `ValueTask` (CA2012 reports it)
  instead of running an `async void`.
- `Tap(null!)` is ambiguous (CS0121): cast the argument, `Tap((Action<T>)null!)`.
- A lambda that only throws (`_ => throw new X()`) binds to the awaited overload: the exception surfaces
  at the await.
- `result.Equals(null)` no longer compiles (the `null` literal binds to `Equals(Result<T>)` through the
  conversion from `Error`): write `result.Equals((object?)null)`.
- `ResultCombineExtensions.Combine(...)` becomes `Result.Combine(...)`.
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
