# /logging-new-analyzer

Scaffold a new Roslyn diagnostic analyzer + code fix in `MicroKit.Logging.Analyzers`.

## Usage

```
/logging-new-analyzer <DiagnosticId> "<Title>"
```

**Examples:**
```
/logging-new-analyzer MKL0011 "Interpolated string used in log message"
/logging-new-analyzer MKL0021 "Non-canonical log property name"
/logging-new-analyzer MKL0031 "Sensitive data in log property name"
```

## What This Command Does

1. Validates the `DiagnosticId` against the registry in `.claude-context/standards/logging-event-ids.md`
2. Assigns the ID if it's new (next available in the category)
3. Scaffolds the analyzer class with `DiagnosticDescriptor`
4. Scaffolds the code fix provider
5. Scaffolds the test file with positive and negative cases
6. Updates the analyzer registry

## Steps

```
1. Load .claude-context/standards/logging-event-ids.md — validate/assign ID
2. Load .claude-context/templates/logging-analyzer-template.md
3. Determine category from ID prefix:
   - MKL001x → MicroKit.Logging.Usage
   - MKL002x → MicroKit.Logging.Performance
   - MKL003x → MicroKit.Logging.Security
4. Scaffold {DiagnosticId}Analyzer.cs:
   - static readonly DiagnosticDescriptor Rule
   - Override Initialize() with specific SyntaxKind registration
   - Use IOperation API, not raw syntax where possible
5. Scaffold {DiagnosticId}CodeFixProvider.cs:
   - RegisterCodeFixesAsync implementation
   - Action-oriented title: "Use structured logging template"
   - FixAllProvider = WellKnownFixAllProviders.BatchFixer
6. Scaffold {DiagnosticId}Tests.cs:
   - AnalyzerTest_Triggers_WhenViolationPresent
   - AnalyzerTest_DoesNotTrigger_WhenCodeIsValid
   - CodeFixTest_Applies_ExpectedTransformation
7. Register analyzer in MicroKit.Logging.Analyzers entry point
8. Update .claude-context/standards/logging-event-ids.md with new entry
9. Run: Use agent logging-analyzer-reviewer to validate
```

## Constraints

- No mutable state on analyzer classes (Roslyn analyzers are instantiated once)
- `foreach` over `ImmutableArray<T>` — never LINQ on analyzer hot paths
- Every `Warning`/`Error` diagnostic must have a code fix
- `DefaultSeverity`: `Error` only for misuse that causes runtime failure; otherwise `Warning`
