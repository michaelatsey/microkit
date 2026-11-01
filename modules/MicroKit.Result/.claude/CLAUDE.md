# MicroKit.Result — Project Brain

## 🎯 Mission
Librairie .NET 10+ pour la gestion explicite des résultats, erreurs et workflows fonctionnels.
Remplace le pattern exception-as-flow par des types expressifs et composables.

## 🛡️ Garanties du module

Chaque règle, skill, commande et agent de ce dossier sert ces cinq garanties. Où le code s'en écarte, ce fichier le dit et nomme la phase de #184 qui traite l'écart.

- **Correctness : un résultat invalide n'est jamais lu comme un succès.**
  - `default(Result<T>)` n'est ni succès ni échec : `Value`, `Error`, `Map`, `Bind`, `Match` et `Ensure` lèvent `ResultException`, `TryGetValue` retourne `false`, `Tap` ne fait rien, `GetValueOrDefault(x)` retourne `x`. Sur `default(Result)`, `Error` et `Match` lèvent aussi `ResultException`.
  - Écarts : `Result.Success<T>(null)` est un succès, et `Error.Category` vaut `Technical` quand elle n'est pas surchargée. La Phase 3 tranche les deux.
- **Security : les détails internes n'atteignent jamais un client par défaut.**
  - `Error.Message` part dans `ProblemDetails.Detail` : il reste sans danger pour le client. Les détails internes vont dans le log ou dans `Metadata`, que ni `ProblemDetails` ni le JSON n'émettent.
  - Écart : `Try` / `TryAsync` sans mapper écrivent `Exception.Message` dans `Detail`. La Phase 2 le remplace par un message générique.
- **Predictability : le synchrone et l'asynchrone se comportent de la même façon.**
  - Écarts : le `Result` non générique n'a que `Match`, `Tap`, `TapError` et `Finally`, et aucun async hors `TryAsync` (Phase 4). `Try` et `TryAsync` capturent tous deux `OperationCanceledException` (Phase 2).
- **Interoperability : les préoccupations HTTP restent hors du cœur.**
  - `MicroKit.Result` ne référence pas ASP.NET Core (test `Core_DoesNotReferenceAspNetCore`) ; le mapping HTTP vit dans `MicroKit.Result.AspNetCore`.
  - Écarts : la doc XML d'`ErrorCategory` cite des statuts HTTP, et la Phase 3 fixe le rôle de `Category`. Le contrat HTTP lui-même évolue en Phases 2 et 5.
- **Performance : toute affirmation est mesurée.**
  - Écart : aucun benchmark n'existe, `tests/MicroKit.Result.PerformanceTests` ne contient que son `.csproj`. La Phase 7 publie les mesures. D'ici là, une affirmation de performance est une hypothèse et se présente comme telle.

## 🏛️ Philosophie Architecturale

### Railway-Oriented Programming (ROP)
Tous les flux passent sur deux rails : **Success** | **Failure**.
Jamais d'exception pour un flux attendu. Les exceptions restent pour l'inattendu.

### Hiérarchie des types
```
Result          ← sans valeur (commandes, side-effects)
Result<T>       ← avec valeur (queries, transformations)
Unit            ← void-safe pour les génériques
```

### Règle d'or
> **Si c'est prévisible → Result. Si c'est imprévisible → Exception.**

## 📐 Conventions de Code

### Naming
- Types: `PascalCase`, `sealed` par défaut
- Méthodes d'extension: verbes fonctionnels (`Map`, `Bind`, `Match`, `Tap`, `Ensure`)
- Erreurs: noun phrases (`UserNotFoundError`, `PaymentDeclinedError`) ; la librairie ne fournit que `ValidationError` et `ErrorCollection`, les erreurs de domaine dérivent de `Error`
- Codes d'erreur: `DOMAIN.ENTITY.ACTION` ex: `AUTH.USER.NOT_FOUND`

### Style C# 2026
- Primary constructors partout où pertinent
- File-scoped namespaces obligatoires
- Nullable Reference Types: `enable`
- `readonly record struct` pour les value objects sans héritage
- `sealed record` pour les types référence fermés
- `ValueTask<T>` pour l'async (convention racine). Son gain face à `Task<T>` n'est pas mesuré ; la Phase 7 de #184 le mesure

### Interdictions
- ❌ `throw` dans les flux métier normaux
- ❌ `null` retourné depuis une méthode publique (utiliser `Result` ou `Result<T>`)  
- ❌ `Exception` pour représenter une validation ou une règle métier
- ❌ Réflexion runtime sauf dans la couche Serialization, annotée `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`
- ❌ `static` mutable partagé (thread-safety)

### Ce qu'aucun sample ni instruction n'enseigne
Un contre-exemple marqué ❌ peut les montrer : il définit ce qu'une review bloque.

- ❌ Le texte d'une exception envoyé à un client
- ❌ Une annulation demandée par l'appelant convertie en échec
- ❌ Un `Result` retourné puis ignoré
- ❌ `.Value` lu sans vérification préalable du succès, y compris dans un property pattern
- ❌ Un `ValueTask` bloqué (`.Result`, `.GetAwaiter().GetResult()` avant complétion, CA2012)
- ❌ CA1848 supprimée : on journalise par `[LoggerMessage]`

## 🧱 Structure des Erreurs

```csharp
// Toujours créer des erreurs typées pour le domaine
public sealed record UserNotFoundError(Guid UserId) 
    : Error(ErrorCode.From("USER.NOT_FOUND"), $"User {UserId} not found")
{
    public override ErrorCategory Category => ErrorCategory.NotFound;
}
```

Les patterns autorisés (`Map`, `Bind`, `Match`, `Tap`, `Ensure`) sont dans `rules/result-patterns.md`, les frontières d'exception dans `rules/no-exceptions-flow.md`, les conventions de test dans `rules/testing.md`.

## 🚀 Performance

Hypothèses non mesurées, que la Phase 7 de #184 confirme ou infirme :

1. `Result<T>` étant un struct, `Map` et `Bind` n'allouent rien sur le chemin succès avec une lambda sans capture ; une lambda qui capture alloue à chaque appel
2. `ValueTask<T>` évite une allocation quand le résultat est disponible de façon synchrone

Fait de conception : `ErrorCollection` est matérialisée à la construction (`ImmutableArray<IError>`, `Message` calculé une fois) ; ne la créer que sur le rail d'échec.

## ✅ Vérification et gates

La vérification a lieu à la fin de chaque étape, pas à chaque édition. Depuis la racine du dépôt :

```bash
dotnet build modules/MicroKit.Result/MicroKit.Result.slnx -c Release
dotnet test  modules/MicroKit.Result/MicroKit.Result.slnx -c Release
dotnet build MicroKit.slnx -c Release -p:WarningsNotAsErrors=NU1902%3BNU1903   # garde : aucun autre module ne casse (#161)
dotnet format modules/MicroKit.Result/MicroKit.Result.slnx --verify-no-changes
```

`-c Release` toujours : `TreatWarningsAsErrors`, nullable compris, ne s'applique qu'en Release. `dotnet format` échoue aujourd'hui sur `src/MicroKit.Result/Validation/ValidationError.cs:99-101`, déjà présent sur `main` ; la CI ne le lance pas. À partir de la Phase 1, `dotnet test ... --collect:"XPlat Code Coverage"` mesure une couverture de lignes d'au moins 90 % sur `src/MicroKit.Result`.

Gates de review, chacun en contexte neuf :

| Gate | Quand |
|---|---|
| `code-review` | Avant le push et sur la pull request |
| `security-review` | Tout changement sous `src/` |
| `dependency-guardian` | Tout changement d'un `.csproj` ou de `Directory.Packages.props` |
| `result-architect` | Tout changement de la surface publique ; ce module n'a pas d'agent `api-reviewer`, `result-architect` tient ce gate |

Avant une pull request :

- XML docs sur tout nouveau membre `public`
- Un test par chemin nouveau, succès et échec
- `CHANGELOG.md` à jour en cas de rupture, `README.md` pour toute nouvelle API publique majeure
- Aucune version dans un `.csproj` : versions et releases relèvent d'ADR-GLOBAL-002
- Pas de `TODO` sans issue : `// TODO(#184): ...`

## 📦 Packages
- `MicroKit.Result` — core (zéro dépendance) ; inclut les convertisseurs JSON (namespace `MicroKit.Result.Serialization`)
- `MicroKit.Result.AspNetCore` — extensions HTTP/ProblemDetails

## 🔗 Références
- [Railway Oriented Programming - Scott Wlaschin](https://fsharpforfunandprofit.com/rop/)
- [Error handling in .NET — Microsoft](https://learn.microsoft.com/dotnet/standard/exceptions/)
- [OneOf library](https://github.com/mcintyre321/OneOf) — inspiration pattern
- [FluentResults](https://github.com/altmann/FluentResults) — inspiration API
