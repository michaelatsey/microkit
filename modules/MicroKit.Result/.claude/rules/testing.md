# Rule: Testing — MicroKit.Result

## Toujours actif pour tout projet de test dans ce module.

## Bibliothèque d'assertions : Shouldly (obligatoire)

Voir la règle globale : `.claude/rules/testing-libraries.md` (racine du monorepo).

**La bibliothèque d'assertions interdite, et la raison (licence), sont dans la règle globale ci-dessus.**
**Shouldly est obligatoire** — MIT, syntaxe : `result.ShouldBe(expected)`.

## Conventions de test Result

- Test class naming: `[Type]Tests` — `ResultTests`, `ErrorTests`
- Method naming: `Method_Scenario_ExpectedResult`
- `[Fact]` pour les tests déterministes, `[Theory]` + `[InlineData]` pour les cas paramétrés
- Classes de test `sealed` — pas d'héritage dans les classes de test

## Spécificités Result Testing

- Toujours tester le chemin succès ET le chemin échec pour chaque opération
- Vérifier `IsSuccess`, `IsFailure`, `Value`, `Error` de façon indépendante
- Tester les transformations : `Map`, `Bind`, `MapError`, etc.
- Les exceptions en Result : ne jamais throw ; capturer via `Result.Try` / `Result<T>.Try` (ou `TryAsync`) et représenter l'échec par `Result.Failure`
