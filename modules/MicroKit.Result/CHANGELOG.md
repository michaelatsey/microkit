# Changelog — MicroKit.Result

All notable changes to this package are documented here.
Format: [Keep a Changelog](https://keepachangelog.com/en/1.0.0/) — [Semantic Versioning](https://semver.org/).

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
- `ExceptionError`, `OperationCancelledError` — infrastructure edge cases
- `ResultException` for safe unwrap-or-throw scenarios
- Sync extensions: `Map`, `Bind`, `Tap`, `TapError`, `Ensure`, `Match`, `MapError`, `Compensate`, `Finally`
- `AsyncResultExtensions` — full `ValueTask<Result<T>>` pipeline (all nine operations)
- `ResultCombineExtensions` — `Combine` (fail-fast) and `CombineAll` (collect-all)
- `ResultEnumerableExtensions` — `FirstOrFailure`, `WhereSuccess`, `SelectResults`
- `ResultLinqExtensions` — LINQ query syntax (`from x in result select ...`)
- `ResultFactory` — `Result.Try()` and `Result.TryAsync()` for exception boundary wrapping
- `ValidationError` and `ValidationResult` — structured validation support
- `ErrorExtensions` — `WithMetadata`, `ToCollection`, `IsCategory`, `IsSeverity`
- **MicroKit.Result.AspNetCore**: `ResultHttpExtensions`, `ResultProblemDetailsFactory`
  - Automatic `ErrorCategory` → HTTP status code mapping (404/422/401/403/409/429/500/503)
- **Serialization**: `ResultJsonConverter` and `ResultJsonConverterFactory` — NativeAOT-safe JSON round-trip

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

[1.0.0-preview.1]: https://github.com/michaelatsey/MicroKit/releases/tag/result-v1.0.0-preview.1
