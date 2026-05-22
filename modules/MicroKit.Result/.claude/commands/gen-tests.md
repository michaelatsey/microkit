# Command: /gen-tests

## Usage
```
/gen-tests <FilePath> [--coverage <unit|integration|all>] [--bench]
```

## Description
Génère les tests manquants pour un fichier C# en analysant les membres publics non testés.
Utilise l'agent `test-generator` pour la logique de génération.

## Exemple
```
/gen-tests src/MicroKit.Result/Extensions/ResultExtensions.cs
/gen-tests src/MicroKit.Result/Core/Result{T}.cs --coverage all --bench
```

## Process
1. Analyser tous les membres `public` du fichier source
2. Détecter les tests existants dans `tests/` pour ce fichier
3. Identifier les membres non couverts
4. Générer les tests manquants avec AAA structure
5. Si `--bench` : générer un fichier BenchmarkDotNet correspondant

## Output
- `tests/MicroKit.Result.Tests/{Namespace}/{FileName}Tests.cs`
- (si --bench) `benchmarks/MicroKit.Result.Benchmarks/{FileName}Benchmarks.cs`

## Règles de génération
- Un `[Fact]` par cas — pas de `[Fact]` avec plusieurs assertions sur des chemins différents
- `[Theory]` avec `[InlineData]` pour les variantes de valeurs
- Toujours tester: success path, failure path, null args, chained behavior
- FluentAssertions custom: `Should().BeSuccess()`, `Should().BeFailure()`
- Pas de `Thread.Sleep` — utiliser `Task.Delay` ou fake time
- Noms clairs: `MethodName_WhenCondition_ThenExpected`
