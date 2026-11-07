# Command: /gen-tests

## Usage
```
/gen-tests <FilePath>
```

## Description
Génère les tests unitaires manquants pour un fichier C# en analysant les membres publics non testés.
Utilise l'agent `test-generator` pour la logique de génération. Les benchmarks n'en sont pas : ils vivent dans `tests/MicroKit.Result.PerformanceTests` (BenchmarkDotNet).

## Exemple
```
/gen-tests src/MicroKit.Result/Extensions/ResultExtensions.cs
/gen-tests src/MicroKit.Result/ResultOfT.cs
```

## Process
1. Analyser tous les membres `public` du fichier source
2. Détecter les tests existants dans `tests/` pour ce fichier
3. Identifier les membres non couverts
4. Générer les tests manquants avec AAA structure
5. Lancer les tests générés, et rapporter leur résultat et la couverture de lignes

## Output
- `tests/MicroKit.Result.UnitTests/{Dossier}/{FileName}Tests.cs`

## Règles de génération
- Un `[Fact]` par cas — pas de `[Fact]` avec plusieurs assertions sur des chemins différents
- `[Theory]` avec `[InlineData]` pour les variantes de valeurs
- Toujours tester, selon #184 Phase 1 (`rules/testing.md`) : success path, failure path, `default(Result)` et `default(Result<T>)`, null args, chained behavior
- Lois de `Map` et `Bind` en tests de propriétés (FsCheck, ajouté par la Phase 1)
- Un écart qu'une phase ultérieure de #184 traite se teste tel qu'il est, sans `Skip` ; la phase qui le change réécrit le test (`rules/testing.md`)
- Aucun `Result` retourné n'est ignoré : il est assigné puis vérifié
- Assertions Shouldly sur l'API de `Result` : `result.IsSuccess.ShouldBeTrue()`, `result.Value.ShouldBe(expected)`, `result.Error.ShouldBeOfType<TError>()`
- Pas de `Thread.Sleep` — utiliser `Task.Delay` ou fake time
- Noms clairs: `Method_Scenario_ExpectedResult` (règle `.claude/rules/testing.md`)
