# /logging-new-generator

Scaffold a new Roslyn source generator in `MicroKit.Logging.Generators`.

## Usage

```
/logging-new-generator <GeneratorName>
```

**Examples:**
```
/logging-new-generator LoggerMessageGenerator
/logging-new-generator OperationContextGenerator
```

## What This Command Does

Scaffolds a new incremental source generator targeting .NET 10 Roslyn APIs — using `IIncrementalGenerator`, not the legacy `ISourceGenerator`.

## Steps

```
1. Load .claude-context/templates/logging-generator-template.md
2. Scaffold {GeneratorName}.cs:
   - [Generator] attribute
   - Implements IIncrementalGenerator
   - Uses IncrementalValueProvider pipeline (not OnVisitSyntaxNode)
   - ForAttributeWithMetadataName for attribute-triggered generation
3. Scaffold the trigger attribute if needed: [GeneratorName]Attribute.cs
4. Scaffold {GeneratorName}Tests.cs using Microsoft.CodeAnalysis.Testing:
   - GeneratesCorrectOutput_ForValidInput
   - DoesNotGenerate_ForInvalidInput
   - GeneratedCode_CompilesWithoutErrors
5. Add to MicroKit.Logging.Generators project
```

## Constraints

- **`IIncrementalGenerator` only** — `ISourceGenerator` is legacy, do not use
- **Incremental pipeline** — all data extraction must go through `IncrementalValueProvider` transformations
- **No `Compilation` access** in the incremental pipeline — use `ForAttributeWithMetadataName` or `SyntaxValueProvider`
- **Generated code must be deterministic** — same input always produces same output (required for caching)
- **Generated files naming** — `{TypeName}.g.cs` convention
- **No diagnostics in generators** — report errors via `DiagnosticDescriptor` registered in Analyzers, not in the generator
