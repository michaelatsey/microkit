# Rule: Testing — MicroKit.Domain

## Toujours actif pour tout projet de test dans ce module.

## Bibliothèque d'assertions : Shouldly (obligatoire)

Voir la règle globale : `.claude/rules/testing-libraries.md` (racine du monorepo).

**FluentAssertions est interdit** — licence commerciale Xceed en v8+.
**Shouldly est obligatoire** — MIT, syntaxe : `result.ShouldBe(expected)`.

## Conventions de test Domain

- Test class naming: `[Type]Tests` — `MoneyTests`, `AuditableAggregateRootTests`
- Method naming: `Method_Scenario_ExpectedResult`
- `[Fact]` pour les tests déterministes, `[Theory]` + `[InlineData]` pour les cas paramétrés
- Classes de test `sealed` — pas d'héritage dans les classes de test
- Les types de test helpers (stubs d'agrégats, d'IDs) sont définis dans le même fichier

## Spécificités Domain Testing

- Tester les invariants via les exceptions DomainException / BusinessRuleViolationException
- Vérifier que les DomainEvents sont correctement émis après chaque mutation
- Vérifier le drain des events après `DrainDomainEvents()`
- ValueObjects : tester la validation du constructeur, l'égalité structurelle, les méthodes métier
