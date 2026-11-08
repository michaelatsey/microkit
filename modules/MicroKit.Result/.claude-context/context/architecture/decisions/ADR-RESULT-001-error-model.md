## ADR-RESULT-001: The error model of MicroKit.Result before stable

**Date:** 2026-11-07
**Status:** Accepted
**Decided by:** Ange-Michaël Atsé (web session, 2026-11-07), recorded by #184 Phase 3
**Phase:** MicroKit.Result — #184 Phase 3; implemented by Phase 4 (and Phases 5, 6 where stated)

### Guarantees

Every choice below is judged against the five guarantees the module commits to (`.claude/CLAUDE.md`):

| Guarantee | Statement |
|---|---|
| **G1 Correctness** | An invalid result is never read as a success. |
| **G2 Security** | Internal details never reach a client by default. |
| **G3 Predictability** | Synchronous and asynchronous operations behave alike. |
| **G4 Interoperability** | HTTP concerns stay out of the core (`MicroKit.Result`); they live in `MicroKit.Result.AspNetCore`. |
| **G5 Performance** | Every performance claim is measured. |

A decision that would break another module's build is weighed against a non-breaking alternative (#184,
"No collateral break"). At the time of writing, `MicroKit.MediatR` uses `ErrorCode.Unauthorized`,
`ErrorCode.Validation` and `ErrorCode.Internal`, and seven `MicroKit.MediatR` test files declare `Error` subtypes that do not
override `Category`. No other module uses `ResultCombineExtensions`, `Try`, `TryAsync`, the async
extensions, the `ErrorCollection` filters or the ASP.NET Core extensions (repository-wide grep, 2026-11-07).

### Context

`IError` carries four dimensions besides its message: `Code`, `Category`, `Severity`, `Metadata`. Their roles
were never stated, so the core's XML docs mapped `Category` to HTTP statuses (against G4), `Metadata` was
emitted by the default serialization of a non-generic `Result` (against G2), and `ErrorCode` constants
duplicated `ErrorCategory` values. Building an error took a new type each time. A forgotten `Category`
override answered HTTP 500. `Result.Success<T>(null)` was a success. The non-generic `Result`, the natural
return type of a command, had four operations. The async extensions took no `CancellationToken` and accepted
only `Task`-returning delegates, while the module's port convention is `ValueTask`.

Phase 2 already fixed the behavior defects (OperationCanceledException, exception text, `default` results,
async-void side effects). This ADR fixes the model.

### Decision

**D1 — The four dimensions have one role each.**
- `Code` is the error's stable identifier: a free string, `DOMAIN.ENTITY.REASON` by convention
  (`AUTH.USER.NOT_FOUND`). Clients and logs may branch on it; it never changes meaning.
- `Category` is the only input to the HTTP status (in `MicroKit.Result.AspNetCore`) and to the category of an
  `ErrorCollection`. It says what kind of failure happened, not which status answers it.
- `Severity` is for logging and observability only. It never changes a status or a control flow.
- `Metadata` is in-process context (correlation ids, entity ids, retry hints). It is never sent to a client
  by default: no `ProblemDetails` member, no JSON converter default writes it (D12).

*G2*: only `Code`, `Message` and `Category` are client-facing, and `Message` is client-safe by contract.
*G4*: the core names kinds of failure; the transport maps them.

**D2 — The predefined `ErrorCode` constants stay, as convenience codes.** `MicroKit.MediatR` uses
`ErrorCode.Unauthorized`, `ErrorCode.Validation` and `ErrorCode.Internal`; removing them breaks its build. Their XML docs say they
are convenience codes for errors without a domain code, not a second taxonomy: `Category`, not `Code`, drives
behavior. No constant is added.

**D3 — Static factories on `Error`, one per category, return a public sealed record.**

```csharp
public sealed record CategorizedError(ErrorCode Code, string Message, ErrorCategory Category) : Error(Code, Message);

public abstract record Error
{
    public static CategorizedError NotFound(ErrorCode code, string message);
    public static CategorizedError Validation(ErrorCode code, string message);
    public static CategorizedError Unauthorized(ErrorCode code, string message);
    public static CategorizedError Forbidden(ErrorCode code, string message);
    public static CategorizedError Conflict(ErrorCode code, string message);
    public static CategorizedError TooManyRequests(ErrorCode code, string message);
    public static CategorizedError Technical(ErrorCode code, string message);
    public static CategorizedError Unavailable(ErrorCode code, string message);
    public static CategorizedError External(ErrorCode code, string message);
    public static CategorizedError BusinessRule(ErrorCode code, string message);
    public static CategorizedError NotSupported(ErrorCode code, string message);
    public static CategorizedError Timeout(ErrorCode code, string message);
    public static CategorizedError Cancelled(ErrorCode code, string message);
    public static CategorizedError PreconditionFailed(ErrorCode code, string message);
}
```

`message` is validated (`ArgumentException.ThrowIfNullOrWhiteSpace`). The factories serve boundaries and
ad-hoc errors; a domain error stays one type each (`UserNotFoundError`), which keeps strong typing in
`Match` and pattern matching. `CategorizedError` is a value: two with the same code, message and category are
equal. *G1*: the category is explicit at the call site, never the `Technical` default.

**D4 — Implicit conversion to `Result<T>` and to `Result` from `Error` and from `ErrorCollection`.**

```csharp
public static implicit operator Result<T>(Error error);            // => Failure(error)
public static implicit operator Result<T>(ErrorCollection errors); // => Failure(errors)
public static implicit operator Result(Error error);
public static implicit operator Result(ErrorCollection errors);
```

C# forbids user-defined conversions from an interface, so `IError` cannot convert; `Error` and
`ErrorCollection` (which does not derive from `Error`) are the two concrete roots. A `null` error throws
`ArgumentNullException` (through `Failure`). The conversion never applies to a value that is itself a result:
Phase 2 made `Result<T>.Success` reject one (`ArgumentException`), and the implicit conversion from `T`
goes through `Success`. The non-generic overloads are part of the parity of D9.

Constraints, verified with the compiler:
- For `Result<object>` and any `T` that is not an error type, `Result<object> r = someError;` binds to the
  error operator (the more specific source type): the outcome is a failure. *G1* holds.
- For `T` = `Error` or `ErrorCollection`, the two operators coincide and the conversion is ambiguous
  (CS0457) at the use site.
- For `T` a subtype of `Error` (`Result<UserNotFoundError> r = new UserNotFoundError(id);`), the conversion
  from `T` is the exact match and wins: the outcome is a success whose value is the error.
- So an error used as the value of a result has three outcomes depending on `T`: a failure (`object`,
  `IError`, unrelated `T`), a compile error (`Error`, `ErrorCollection`), a success (a subtype). Code that means
  a value writes `Result.Success(error)`, code that means a failure writes `Result.Failure<T>(error)`; a result
  whose value is an error is a design smell, and Phase 4 documents the three cases on the operators.
- Existing code that compiled through the conversion from `T` changes meaning without a compile error:
  `Result<object> r = someError;` and `Result<IReadOnlyList<IError>> r = errorCollection;` were successes and
  become failures. No other module does this (grep); the CHANGELOG lists it as a break.

The implicit conversion from `T` and `Success` throw `ArgumentException` on a result value. This departs
from the Framework Design Guidelines' "do not throw from implicit casts" on purpose: the alternative reads a
failure as a success (*G1*), and the exception only fires on misuse.

**D5 — Mandatory category: an analyzer warning, not an abstract member.** Making `Error.Category` abstract
breaks `MicroKit.MediatR`'s test build. Phase 6 ships, in `MicroKit.Result.Analyzers`, a warning for a
non-abstract `Error` subtype that neither overrides `Category` nor derives from a type that does
(`CategorizedError`, `ValidationError`, `ExceptionError` and user bases that override it are exempt). The
default stays `Technical` in code; the analyzer is how a forgotten override surfaces. Diagnostic ids follow
`MicroKit.Persistence.Analyzers` (`MKP001`…): `MKR001` (discarded result, the first diagnostic of Phase 6),
`MKR002` (category not overridden).

**D6 — `Success(null)` throws `ArgumentNullException`.** `Result.Success<T>(T value)`, `Result<T>.Success`
and the implicit conversion from `T` (which goes through `Success`) reject `null`, for reference types and
`Nullable<T>` alike. Absence is a failure (`NotFound`), not a null value (*G1*: `IsSuccess` implies a value).
`Result<T>.Try`/`TryAsync` whose delegate returns `null` throw `ArgumentNullException` to the caller, outside
their `catch`: a null return is a contract violation of the delegate, not an infrastructure failure. The JSON
converter reads `"value": null` as an invalid payload (`JsonException`). `Try`/`TryAsync` build their
success through `Success`, which also closes their bypass of the result-as-value check (Phase 2 review).
Applied only if no other module breaks: since a throw at runtime escapes a build, Phase 4 checks the guard
build, greps the other modules for a possibly-null `Success`, and runs the test suites of the modules that
depend on `MicroKit.Result`; any failure withdraws D6 and records it as a gap.

**D7 — `Combine` and `CombineAll` move to static members of `Result`; `ResultCombineExtensions` is removed.**

```csharp
public static Result Combine(params ReadOnlySpan<Result> results);
public static Result CombineAll(params ReadOnlySpan<Result> results);
public static Result<(T1, T2)> Combine<T1, T2>(Result<T1> first, Result<T2> second);
public static Result<(T1, T2, T3)> Combine<T1, T2, T3>(Result<T1> first, Result<T2> second, Result<T3> third);
public static Result CombineAll<T>(params ReadOnlySpan<Result<T>> results);
```

No other module uses `ResultCombineExtensions`; the README and rules already wrote
`ResultCombineExtensions.Combine(…)` only because the class existed. Behavior is Phase 2's (`default` throws
`ResultException`).

**D8 — Async extensions: `Task` and `ValueTask` delegates, `CancellationToken` overloads.**
- Every async operation keeps its `Task`-returning delegate overload, without a token (today's surface).
- Every async operation gains one overload whose delegate takes a `CancellationToken` and returns a
  `ValueTask`, with `CancellationToken ct = default` as last parameter, passed to the delegate:

```csharp
public static ValueTask<Result<TOut>> MapAsync<TIn, TOut>(this Result<TIn> result,
    Func<TIn, CancellationToken, ValueTask<TOut>> mapper, CancellationToken ct = default);
public static ValueTask<Result<TOut>> BindAsync<TIn, TOut>(this Result<TIn> result,
    Func<TIn, CancellationToken, ValueTask<Result<TOut>>> binder, CancellationToken ct = default);
// …and MatchAsync, TapAsync, TapErrorAsync, EnsureAsync, MapErrorAsync, CompensateAsync, FinallyAsync alike,
// on Result<T> and on Result (D9).
```

Constraint, verified with the compiler: two overloads of the same name whose delegates differ only by
`Task<TOut>` versus `ValueTask<TOut>` make every async lambda ambiguous (CS0121), which would break every
existing `MapAsync(async v => …)` call of the published previews' consumers, of the README and of the
samples (no other `MicroKit` module calls the async extensions).
Arity separates the two forms without ambiguity: `async v => …` binds to the `Task` overload,
`async (v, ct) => …` to the `ValueTask` one. `ValueTask` is therefore offered through the token overload,
which is where a module port (`ValueTask<Result<T>> GetAsync(Guid id, CancellationToken ct = default)`) is
called: `result.BindAsync((u, ct) => repository.GetAsync(u.Id, ct), ct)`.

The token is passed to the delegate; the extension itself neither checks it nor catches cancellation:
cancellation propagates (*G3*: the same as a synchronous call that throws). The new overloads exist on
`Result<T>` and `Result` receivers only; on `Task<Result<T>>`/`ValueTask<Result<T>>` the caller awaits first,
which keeps the overload count minimal (`result-architect` reviews it). `Tap`, `TapError` and `Finally` keep
their Phase 2 `Func<…, Task>` overloads; `TapAsync`, `TapErrorAsync`, `FinallyAsync` stay for the published
previews' consumers and for the naming of the other `XxxAsync` operations, so both names remain for the
token-less form.

**D9 — The non-generic `Result` gets the operations `Result<T>` has, sync and async.**
- Sync: `Map<TOut>(Func<TOut>) → Result<TOut>`; `Bind(Func<Result>) → Result`;
  `Bind<TOut>(Func<Result<TOut>>) → Result<TOut>`; `Ensure(Func<bool>, IError) → Result`;
  `MapError(Func<IError, IError>) → Result`; `Compensate(Func<IError, Result>) → Result`; and, from a value
  to a command, `Bind<TIn>(this Result<TIn>, Func<TIn, Result>) → Result`.
- Async: `MapAsync`, `BindAsync`, `MatchAsync`, `TapAsync`, `TapErrorAsync`, `EnsureAsync`, `MapErrorAsync`,
  `CompensateAsync`, `FinallyAsync` on `Result`, in both delegate forms of D8.
- Same rules as `Result<T>`: a `default` `Result` throws `ResultException` where `Result<T>` does; arguments
  are validated at the call.

**D10 — `ValidationResult.ToResult<T>()` returns a failure-only `Result<T>`.** It replaces
`Result.Failure<T>(validation.ToResult().Error)`. On a valid `ValidationResult` it throws
`InvalidOperationException` ("the validation has no error"): it never returns a success without a value or a
`default` (*G1*). `ToResult<T>(T value)` stays for the success-or-failure case.

**D11 — The core's XML docs no longer cite HTTP statuses.** `ErrorCategory`'s members describe the kind of
failure; `Cancelled` describes a cancellation other than the caller's own, which propagates as
`OperationCanceledException` and is never converted into an error. The category → status table is documented
on `ResultProblemDetailsFactory.ToStatusCode`, in `MicroKit.Result.AspNetCore` (*G4*).

**D12 — JSON converters: client-safe by default, `Metadata` on opt-in.** Phase 5 makes the converters keep
`Severity`, `Metadata` and the errors of an `ErrorCollection` across a round trip. A converter registered for
HTTP responses would then send `Metadata` to a client, against D1. Therefore `Metadata` is written only when
the factory is created with an explicit option (for in-process storage, such as `MicroKit.MediatR`'s cache);
the default factory writes `code`, `message`, `category`, `severity` and the inner errors, never `Metadata`.
`severity` and a validation error's `propertyName` thus also reach a client through the JSON converters; neither
is an internal detail (D1 keeps `Severity` out of every decision, not out of sight).
Phase 5 names the option.

*Amended by #195 and #198 (2026-11-08): the option selects a view.* `ResultProblemDetailsFactory` no longer
writes a fault of the system's message (#195 item 1), and a result returned as is reaches a client through the
converters that `Result` and `Result<T>` carry (#195 item 6): the default converters must then say no more than
the problem details do. The option is `ResultJsonView`, on `ResultJsonConverterFactory`, `ResultJsonConverter<T>`
and `ResultJsonConverter`:

- `ResultJsonView.Client`, the default, writes what `ToHttpResult` shows a client: no `Metadata`; the message of
  a fault of the system (`ErrorCategoryExtensions.IsSystemFault()`) is `ErrorCategoryExtensions.SystemFaultMessage`,
  in a collection too; a collection whose category does not describe the input
  (`ErrorCategoryExtensions.DescribesInput()`) names only its errors of that category, nested collections
  flattened; `propertyName` is written only in an input category. A round trip reads that view back.
- `ResultJsonView.Full` writes everything, `Metadata`, every message and every error, for in-process storage that
  never reaches a client; a round trip returns the result as it was.

The core owns both predicates and the text; `MicroKit.Result.AspNetCore` uses them, so the two client views cannot
drift.

### Consequences

- Breaking for consumers (allowed on `preview.*`, ADR-GLOBAL-002 D10): `ResultCombineExtensions` disappears
  (D7); `Success(null)` and `Try` returning `null` throw (D6); `Result<Error>`/`Result<ErrorCollection>`
  conversions become ambiguous, and an error converted to `Result<object>`, `Result<IError>` or another
  unrelated `T` becomes a failure instead of a success (D4).
- Not breaking for other modules' builds, by construction: D2 keeps the constants, D5 avoids the abstract
  member, D8 keeps every existing overload. D6 is applied only if the guard build stays green.
- Overloads added: 14 factories (D3), 4 conversions (D4), 9 token overloads × 2 receivers (D8), the sync
  and async parity set of `Result` (D9). Each is reviewed by `result-architect` in Phase 4. As implemented,
  D9's `Bind` to `Result` and `Bind<TOut>` on `Result` bring their async twins in both forms: 20 token
  overloads, and 11 `Task` overloads for `Result`, `BindAsync<TIn>` → `Result` included.
- The 14 factory names are inherited members of every `Error` subtype: a positional record parameter of the
  same name (`Timeout`, `Conflict`…) fails with CS8866 (verified). The CHANGELOG migration notes say so.
- G5: no decision here claims a performance gain. `ValueTask` delegates follow the module's port convention,
  not a measurement; Phase 7 measures `ValueTask<Result<T>>` against `Task<Result<T>>`.

### Impact on existing decisions

- `.claude/CLAUDE.md` divergence notes "Phase 3" become "ADR-RESULT-001 Dn; Phase 4 applies it".
- ADR-GLOBAL-002 D10 (no `[Obsolete]` before 2.0.0): respected; removals ship outright.
- The root convention (`ValueTask<T>` for async, `CancellationToken ct = default` last) is applied to the new
  token overloads (D8).
