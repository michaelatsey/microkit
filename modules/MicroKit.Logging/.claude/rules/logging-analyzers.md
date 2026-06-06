# Rule: Logging Analyzers

Rules for `MicroKit.Logging.Analyzers` — Roslyn diagnostic analyzers and code fixes.

## Diagnostic ID Registry

All diagnostic IDs are registered in `.claude-context/standards/logging-event-ids.md`.  
**Never assign an ID that is not registered.** Use `/logging-new-analyzer` to get an ID assigned.

| Range | Category |
|-------|----------|
| MKL001x | Structured logging usage |
| MKL002x | Property naming |
| MKL003x | Security / sensitive data |
| MKL004x | Performance |
| MKL005x | API usage |

## Analyzer Class Rules

- **Stateless** — no mutable instance fields. `static readonly DiagnosticDescriptor` only.
- **`IIncrementalGenerator`** — use `IOperation` API, not raw `SyntaxNode` where available
- **No LINQ** in analyzer logic — `foreach` over `ImmutableArray<T>`
- **Register on specific `SyntaxKind`** — avoid `CompilationStart` registrations unless unavoidable
- **`SymbolEqualityComparer.Default`** for all symbol equality checks

## Code Fix Rules

- Every `Warning` or `Error` diagnostic has a corresponding `CodeFixProvider`
- `FixAllProvider = WellKnownFixAllProviders.BatchFixer` always set
- Fix title is action-oriented: "Use structured logging template", "Replace with canonical property name"
- Fix must preserve original semantics — no behavior changes

## Test Coverage

Every analyzer must have tests covering:
1. The exact violation scenario (triggers diagnostic)
2. Valid code (does not trigger)
3. The code fix transformation (before/after)
4. At least one edge case (null argument, nested expression, etc.)
