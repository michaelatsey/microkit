# MicroKit.Result — Project Brain

## 🎯 Mission
Librairie .NET 10+ pour la gestion explicite des résultats, erreurs et workflows fonctionnels.
Remplace le pattern exception-as-flow par des types expressifs et composables.

## 🛡️ Garanties du module

Chaque règle, skill, commande et agent de ce dossier sert ces cinq garanties. Où le code s'en écarte, ce fichier le dit et nomme la phase de #184 qui traite l'écart. Un écart nouveau, qu'aucune phase ne traite, devient sa propre issue ou entre dans #184 avant d'être listé ici. Les autres fichiers peuvent détailler un écart listé ici, jamais en ajouter un qui n'y est pas.

Une note d'écart décrit le comportement actuel. La PR d'une phase retire ou réécrit, dans la même PR, chaque note qu'elle résout : ici, dans `README.md`, `rules/`, `skills/`, `agents/` et `commands/` (`grep -rn "Phase <N>" .claude README.md`). Une note qui décrit un comportement corrigé fait échouer la review de la phase.

- **Correctness : un résultat invalide n'est jamais lu comme un succès.**
  - `default(Result<T>)`, comme `new Result<T>()`, n'est ni succès ni échec : `Value`, `Error`, `Map`, `Bind`, `Match` et `Ensure` lèvent `ResultException`, `TryGetValue` retourne `false`, `Tap` ne fait rien, `GetValueOrDefault(x)` retourne `x`. Sur `default(Result)`, `Error` et `Match` lèvent aussi `ResultException`.
  - Écarts, Phase 2 :
    - `Combine` et `CombineAll` sur des `Result`, et `CombineAll<T>`, lisent un résultat `default` comme un succès ; les surcharges `Combine` à tuple lèvent.
    - La conversion implicite de `T` vers `Result<T>` (`ResultOfT.cs:187`) accepte un `Result` ou un `Result<T>` comme valeur quand `T` est `object` : `Result<object> r = Result.Failure<int>(error);` est un succès.
    - `ResultJsonConverter<T>.Read` fait de `{"isSuccess":true}` sans `value` un succès sans valeur, et ignore un `error` à côté d'un `isSuccess` vrai.
    - `Result<T>.Try` et `TryAsync` avec un mapper qui retourne `null` donnent un échec dont `Error` est `null` (`ResultOfT.cs:139,179`).
    - Les filtres d'`ErrorCollection` (`WithCategory`, `WithSeverity`, `WithCode`, `OfType`, `Flatten`) peuvent construire une collection vide, que `From` interdit.
    - `Successes` et `Failures` ignorent un résultat `default`, que `Partition` refuse en levant ; la Phase 2 les fait traiter de la même façon.
  - Écarts, Phase 3 : `Result.Success<T>(null)` est un succès, et `Error.Category` vaut `Technical` quand elle n'est pas surchargée.
- **Security : les détails internes n'atteignent jamais un client par défaut.**
  - `Error.Message` atteint le client : il reste sans danger pour lui. Les détails internes vont dans le log, pas dans `Metadata` : `ProblemDetails` et `ResultJsonConverter<T>` ne l'émettent pas, mais le `Result` non générique l'émet (ci-dessous) et la Phase 3 décide de son rôle.
  - Écart, Phase 2 : `Try` / `TryAsync` sans mapper font de `Exception.Message` le message de l'erreur. Il atteint le client par `ProblemDetails.Detail` et `errors[].message` (`ToHttpResult`), et par `error.message` (`ResultJsonConverter<T>`). La Phase 2 le remplace par un message générique.
  - Écart, Phase 5 : le `Result` non générique n'a pas de convertisseur JSON. Retourné tel quel par un endpoint, il est sérialisé propriété par propriété : un échec répond 200 avec `error.metadata`, un succès répond 500. Par `ToHttpResult()`, il n'expose rien de cela.
- **Predictability : le synchrone et l'asynchrone se comportent de la même façon.**
  - Écarts, Phase 2 :
    - `Tap`, `TapError` et `Finally` prennent une `Action`, sur `Result<T>` comme sur `Task<Result<T>>` et `ValueTask<Result<T>>` : un lambda async y compile en `async void`, le résultat revient avant l'effet, et une exception de l'effet termine le processus. `TapAsync`, `TapErrorAsync` et `FinallyAsync` l'attendent, mais n'existent que sur `Result<T>`, ni sur `Task<Result<T>>` ni sur `ValueTask<Result<T>>` : on attend d'abord la tâche (`Result<User> user = await GetUserAsync(id, ct);`), puis on appelle `user.TapAsync(...)`.
    - `Try` et `TryAsync` sans mapper capturent `OperationCanceledException` ; la Phase 2 les fait relancer. Avec un mapper, le mapper décide, et `DatabaseExceptionMapper` (`rules/csharp-style.md`) en fait une erreur : `Try` et `TryAsync` ne servent pas un appel annulable, et leur lambda ne capture pas de `CancellationToken`.
    - Les extensions async valident leurs arguments dans leur partie async : `MapAsync(null)` lève à l'`await`, `Map(null)` à l'appel. La Phase 2 les valide avant le premier `await`.
  - Écart, Phase 4 : le `Result` non générique n'a que `Match`, `Tap`, `TapError` et `Finally`, et aucun async hors `TryAsync`.
  - Écarts, Phase 3 (décision de l'ADR) puis Phase 4 (mise en œuvre) :
    - Les délégués async des extensions retournent `Task`, jamais `ValueTask` : un port `ValueTask<Result<T>>` s'y passe par un lambda async, `async x => await port(x, ct)`.
    - Aucune extension async ne prend de `CancellationToken` : un délégué qui en a besoin le capture.
- **Interoperability : les préoccupations HTTP restent hors du cœur.**
  - `MicroKit.Result` ne référence pas ASP.NET Core (test `Core_DoesNotReferenceAspNetCore`) ; le mapping HTTP vit dans `MicroKit.Result.AspNetCore`.
  - Écarts, Phase 3 : la doc XML d'`ErrorCategory` cite des statuts HTTP et décrit `Cancelled` comme une annulation de l'appelant, que les règles interdisent de convertir en erreur ; la Phase 3 fixe le rôle de `Category`.
  - Écart, Phase 5 : `ResultJsonConverter<T>` perd `Severity`, `Metadata` et les erreurs d'une `ErrorCollection` ; il désérialise une erreur en un type privé, et lève une exception sans rapport en sérialisant un `default`. La Phase 5 conserve ces champs sur un aller-retour, et fait échouer la sérialisation d'un `default` avec une erreur claire.
  - Écarts, Phases 2 et 5 : le contrat HTTP lui-même (`rules/result-patterns.md`). En Phase 5 aussi, le commentaire de `ResultProblemDetailsFactory.cs:65` dit qu'aucune constante `StatusCodes` n'existe pour 499, alors que `StatusCodes.Status499ClientClosedRequest` existe.
- **Performance : toute affirmation est mesurée.**
  - Écart : aucun benchmark n'existe, `tests/MicroKit.Result.PerformanceTests` ne contient que son `.csproj`. La Phase 7 publie les mesures. D'ici là, une affirmation de performance est une hypothèse et se présente comme telle.
  - Écart, Phase 7 : le constructeur d'`ErrorCollection` construit `Message` par LINQ (`ErrorCollection.cs:27`), ce que la checklist de `agents/dotnet-reviewer.md` bloque dans un nouveau code. La Phase 7 mesure ce chemin, et il ne change que si la mesure en montre le coût.
  - Écart, Phase 2 : la compatibilité NativeAOT n'est pas vérifiée. Seul l'analyseur de trimming tourne (`IsTrimmable`, `Directory.Build.props` racine) ; la Phase 2 met `IsAotCompatible` dans `src/MicroKit.Result/MicroKit.Result.csproj` et `src/MicroKit.Result.AspNetCore/MicroKit.Result.AspNetCore.csproj`, et l'analyseur AOT la garde. D'ici là, `README.md` la présente comme non vérifiée.

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

`-c Release` toujours : `TreatWarningsAsErrors`, nullable compris, ne s'applique qu'en Release. `dotnet format` échoue aujourd'hui sur `src/MicroKit.Result/Validation/ValidationError.cs:99-101`, déjà présent sur `main`, et seulement là : toute autre ligne signalée est nouvelle. La Phase 2 corrige ces lignes ; la CI ne lance pas `dotnet format`.

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
- [Railway Oriented Programming - Scott Wlaschin](https://fsharpforfunandprofit.com/rop/)
- [Error handling in .NET — Microsoft](https://learn.microsoft.com/dotnet/standard/exceptions/)
- [OneOf library](https://github.com/mcintyre321/OneOf) — inspiration pattern
- [FluentResults](https://github.com/altmann/FluentResults) — inspiration API
