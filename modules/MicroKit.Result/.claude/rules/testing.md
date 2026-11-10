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
- `default(Result)` et `default(Result<T>)` : ni succès ni échec, `Error` et `Match` lèvent `ResultException` (garantie correctness, `.claude/CLAUDE.md`)
- Les exceptions en Result : ne jamais throw ; capturer via `Result.Try` / `Result<T>.Try` (ou `TryAsync`) avec un mapper (`rules/no-exceptions-flow.md`) et représenter l'échec par `Result.Failure`
- Un `Result` retourné est toujours assigné puis vérifié, jamais ignoré

```csharp
// Assert : les deux rails, chacun dans son test
result.IsSuccess.ShouldBeTrue();
result.Value.ShouldBe(expected);

result.IsFailure.ShouldBeTrue();
result.Error.ShouldBeOfType<UserNotFoundError>();
```

## Socle : #184 Phase 1

Livré par la Phase 1 ; chaque nouvelle opération y ajoute ses tests.

- Tests unitaires de `Result`, `Result<T>`, chaque extension de `Extensions/`, `ErrorCollection`, `ValidationResult`, `ValidationError`, `ErrorMetadataBuilder` et du convertisseur JSON : les deux rails de chaque opération, plus `default(Result)` et `default(Result<T>)`.
- Tests de propriétés, au moins 100 cas chacun, pour les lois ci-dessous (`Properties/MonadLawTests.cs`) : FsCheck.Xunit 3.6.0, l'intégration xunit v2 qui correspond à xunit 2.9.3, avec `[Property(MaxTest = 100)]` écrit explicitement.
- Les tests décrivent le comportement actuel. Les défauts de la Phase 2 de #184 ont été testés sous leur forme corrigée, avec un `Skip` que la Phase 2 a retiré ; ces tests passent.
- Un écart qu'une phase ultérieure traite (3 à 7) se teste tel qu'il est, sans `Skip` : son comportement corrigé n'est pas encore livré, ou pas encore décidé, et la phase qui le change réécrit le test.
- Couverture de lignes de `src/MicroKit.Result` d'au moins 90 %.

```
Map, identité       : r.Map(x => x) == r
Map, composition    : r.Map(f).Map(g) == r.Map(x => g(f(x)))
Bind, neutre gauche : Result.Success(a).Bind(f) == f(a)
Bind, neutre droit  : r.Bind(x => Result.Success(x)) == r
Bind, associativité : r.Bind(f).Bind(g) == r.Bind(x => f(x).Bind(g))
```
