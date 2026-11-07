# MicroKit.Result — Project Brain

## 🎯 Mission
Librairie .NET 10+ pour la gestion explicite des résultats, erreurs et workflows fonctionnels.
Remplace le pattern exception-as-flow par des types expressifs et composables.

## 🛡️ Garanties du module

Chaque règle, skill, commande et agent de ce dossier sert ces cinq garanties. Où le code s'en écarte, ce fichier le dit et nomme la phase de #184 qui traite l'écart. Un écart nouveau, qu'aucune phase ne traite, devient sa propre issue ou entre dans #184 avant d'être listé ici. Les autres fichiers peuvent détailler un écart listé ici, jamais en ajouter un qui n'y est pas.

Une note d'écart décrit le comportement actuel. La PR d'une phase retire ou réécrit, dans la même PR, chaque note qu'elle résout : ici, dans `README.md`, `rules/`, `skills/`, `agents/` et `commands/` (`grep -rn "Phase <N>" .claude README.md`). Une note qui décrit un comportement corrigé fait échouer la review de la phase.

- **Correctness : un résultat invalide n'est jamais lu comme un succès.**
  - `default(Result<T>)`, comme `new Result<T>()`, n'est ni succès ni échec : `Value`, `Error`, `Map`, `Bind`, `Match` et `Ensure` lèvent `ResultException`, `TryGetValue` retourne `false`, `Tap` ne fait rien, `GetValueOrDefault(x)` retourne `x`. Sur `default(Result)`, `Error` et `Match` lèvent aussi `ResultException`. `Combine`, `CombineAll`, `Partition`, `Successes` et `Failures` lèvent `ResultException` sur un résultat `default`.
  - La conversion implicite de `T` vers `Result<T>` refuse un `Result` ou un `Result<T>` comme valeur (`ArgumentException`), possible quand `T` est `object`. `ResultJsonConverter<T>.Read` refuse (`JsonException`) un contenu qui ne décrit pas un résultat valide : succès sans `value` ou avec `error`, échec sans `error` ou avec `value`, `isSuccess` en double, `code` vide. Un mapper de `Try` / `TryAsync` qui retourne `null` lève `ArgumentNullException`. Les filtres d'`ErrorCollection` retournent une liste, jamais une collection vide.
  - Écarts : `Result.Success<T>(null)` est un succès (ADR-RESULT-001 D6, Phase 4 le fait lever `ArgumentNullException`), et `Error.Category` vaut `Technical` quand elle n'est pas surchargée (ADR-RESULT-001 D5, Phase 6 ajoute l'avertissement d'analyseur).
- **Security : les détails internes n'atteignent jamais un client par défaut.**
  - `Error.Message` atteint le client : il reste sans danger pour lui. Les détails internes vont dans le log, pas dans `Metadata` : `ProblemDetails` et `ResultJsonConverter<T>` ne l'émettent pas, mais le `Result` non générique l'émet (ci-dessous) et ADR-RESULT-001 D1 en fait un contexte en mémoire, jamais envoyé au client par défaut.
  - `Try` / `TryAsync` sans mapper construisent une `ExceptionError` : code `ErrorCode.Exception`, message générique (`"An unexpected error occurred."`), aucune métadonnée ; l'exception reste en mémoire (`ExceptionError.Exception`, `[JsonIgnore]`) pour le log.
  - Écart, Phase 5 : le `Result` non générique n'a pas de convertisseur JSON. Retourné tel quel par un endpoint, il est sérialisé propriété par propriété : un échec répond 200 avec `error.metadata`, un succès répond 500. Par `ToHttpResult()`, il n'expose rien de cela.
- **Predictability : le synchrone et l'asynchrone se comportent de la même façon.**
  - `Tap`, `TapError` et `Finally` donnés avec un délégué qui retourne une `Task`, lambda async compris, se lient à une surcharge qui attend l'effet et retourne un `ValueTask<Result<T>>` (ou `ValueTask<Result>`), sur `Result<T>`, `Result`, `Task<Result<T>>` et `ValueTask<Result<T>>`. Une `Action` garde la surcharge synchrone.
  - `Try` et `TryAsync` sans mapper relancent `OperationCanceledException`. Avec un mapper, le mapper reçoit toute exception et décide, et `DatabaseExceptionMapper` (`rules/csharp-style.md`) en fait une erreur : `Try` et `TryAsync` ne servent pas un appel annulable, et leur lambda ne capture pas de `CancellationToken`.
  - Les extensions async et `TryAsync` valident leurs arguments à l'appel, avant tout `await`, la tâche source comprise.
  - Écart, Phase 4 : le `Result` non générique n'a que `Match`, `Tap`, `TapError` et `Finally`, et aucun async hors `TryAsync` et les surcharges attendues de `Tap`, `TapError` et `Finally`.
  - Écart, Phase 4 : un lambda non async qui retourne un `ValueTask` (`u => cache.SetAsync(u)`) se lie à la surcharge `Action` de `Tap`, `TapError` ou `Finally`, et son `ValueTask` est abandonné ; CA2012 le signale, une erreur en Release. On l'écrit `async u => await cache.SetAsync(u)`. Une surcharge `Func<T, ValueTask>` à côté de `Func<T, Task>` rendrait tout lambda async ambigu (CS0121) : ADR-RESULT-001 D8 en tient compte.
  - Écarts, ADR-RESULT-001 D8, que la Phase 4 applique :
    - Les délégués async des extensions retournent `Task`, jamais `ValueTask` : un port `ValueTask<Result<T>>` s'y passe par un lambda async, `async x => await port(x, ct)`.
    - Aucune extension async ne prend de `CancellationToken` : un délégué qui en a besoin le capture.
- **Interoperability : les préoccupations HTTP restent hors du cœur.**
  - `MicroKit.Result` ne référence pas ASP.NET Core (test `Core_DoesNotReferenceAspNetCore`) ; le mapping HTTP vit dans `MicroKit.Result.AspNetCore`. La catégorie d'une `ErrorCollection` préfère une faute du système (`Technical`, `Unavailable`, `External`, `NotSupported`) sans connaître HTTP ; un test d'intégration garde cette liste alignée sur les catégories que `MicroKit.Result.AspNetCore` mappe en 5xx.
  - Écart, ADR-RESULT-001 D1 et D11, que la Phase 4 applique : la doc XML d'`ErrorCategory` cite des statuts HTTP et décrit `Cancelled` comme une annulation de l'appelant, que les règles interdisent de convertir en erreur.
  - Écart, Phase 5 : `ResultJsonConverter<T>` perd `Severity`, `Metadata` et les erreurs d'une `ErrorCollection` ; il désérialise une erreur en un type privé, et lève une exception sans rapport en sérialisant un `default`. La Phase 5 conserve ces champs sur un aller-retour, et fait échouer la sérialisation d'un `default` avec une erreur claire.
  - Écart, Phase 5 : le contrat HTTP lui-même (`rules/result-patterns.md`). Le commentaire de `ResultProblemDetailsFactory.cs:75` dit aussi qu'aucune constante `StatusCodes` n'existe pour 499, alors que `StatusCodes.Status499ClientClosedRequest` existe.
- **Performance : toute affirmation est mesurée.**
  - Écart : aucun benchmark n'existe, `tests/MicroKit.Result.PerformanceTests` ne contient que son `.csproj`. La Phase 7 publie les mesures. D'ici là, une affirmation de performance est une hypothèse et se présente comme telle.
  - Écart, Phase 7 : le constructeur d'`ErrorCollection` construit `Message` par LINQ (`ErrorCollection.cs:26`), ce que la checklist de `agents/dotnet-reviewer.md` bloque dans un nouveau code. La Phase 7 mesure ce chemin, et il ne change que si la mesure en montre le coût.
  - `IsAotCompatible` est actif dans `src/MicroKit.Result/MicroKit.Result.csproj` et `src/MicroKit.Result.AspNetCore/MicroKit.Result.AspNetCore.csproj` : les analyseurs de trimming, de single-file et d'AOT tournent en Release, sans avertissement ; la réflexion reste confinée aux convertisseurs JSON, annotés `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`.

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
- `readonly record struct` pour les value objects sans héritage (`ErrorCode`, `Unit`) : ce module déroge ici à la convention racine, `sealed record` pour les value objects
- `sealed record` pour les types référence fermés
- `ValueTask<T>` pour l'async (convention racine). Son gain face à `Task<T>` n'est pas mesuré ; la Phase 7 de #184 le mesure

### Interdictions
- ❌ `throw` dans les flux métier normaux
- ❌ `null` retourné depuis une méthode publique (utiliser `Result` ou `Result<T>`), hors un accès dont le nom dit qu'il peut ne rien trouver : `TryGetMetadata<T>`, `GetValueOrDefault()`, `ErrorCollection.FirstOrDefault`
- ❌ `Exception` pour représenter une validation ou une règle métier
- ❌ Réflexion runtime sauf dans la couche Serialization, annotée `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`
- ❌ `static` mutable partagé (thread-safety)

`samples/MicroKit.Result.Samples` est une démo console : ce module y permet `Console.WriteLine`, par une règle qui lui est propre. La convention racine l'interdit sans exception ; ailleurs dans le module, elle s'applique. Ses ports simulés retournent `Task` là où un délégué async des extensions l'exige.

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

Les patterns autorisés (`Map`, `Bind`, `Match`, `Tap`, `Ensure`, `MapError`, `Compensate`) sont dans `rules/result-patterns.md`, les frontières d'exception dans `rules/no-exceptions-flow.md`, les conventions de test dans `rules/testing.md`.

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

`-c Release` toujours : `TreatWarningsAsErrors`, nullable compris, ne s'applique qu'en Release. `dotnet format --verify-no-changes` passe sur tout le module : une ligne qu'il signale est nouvelle. La CI ne lance pas `dotnet format`.

À partir de la Phase 1, la couverture de lignes de `src/MicroKit.Result` est d'au moins 90 %. `dotnet test ... --collect:"XPlat Code Coverage"` ne l'impose pas : il sort 0 quel que soit le taux, et écrit un `coverage.cobertura.xml` par projet de test, sous `TestResults/`. Le chiffre est l'attribut `line-rate` du package `MicroKit.Result` dans le fichier de `MicroKit.Result.UnitTests`.

Gates de review, chacun en contexte neuf :

| Gate | Quand |
|---|---|
| `code-review` | Avant le push et sur la pull request |
| `security-review` | Tout changement sous `src/` |
| `dependency-guardian` | Tout changement d'un `.csproj` ou de `Directory.Packages.props` |
| `result-architect` | Tout changement de la surface publique ; ce module n'a pas d'agent `api-reviewer`, `result-architect` tient ce gate. Il n'a pas Bash : l'appelant lui passe le diff (`git diff main...HEAD -- src/`) |

`dotnet-reviewer` n'est pas un gate : `/review-perf` l'appelle, ou une demande explicite.

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
- [ADR-RESULT-001 — le modèle d'erreur](../.claude-context/context/architecture/decisions/ADR-RESULT-001-error-model.md) : rôle de `Code`, `Category`, `Severity`, `Metadata`, factories, conversions, async et parité du `Result` non générique
- [Railway Oriented Programming - Scott Wlaschin](https://fsharpforfunandprofit.com/rop/)
- [Error handling in .NET — Microsoft](https://learn.microsoft.com/dotnet/standard/exceptions/)
- [OneOf library](https://github.com/mcintyre321/OneOf) — inspiration pattern
- [FluentResults](https://github.com/altmann/FluentResults) — inspiration API
