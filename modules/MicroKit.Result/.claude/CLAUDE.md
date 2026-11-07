# MicroKit.Result — Project Brain

## 🎯 Mission
Librairie .NET 10+ pour la gestion explicite des résultats, erreurs et workflows fonctionnels.
Remplace le pattern exception-as-flow par des types expressifs et composables.

## 🛡️ Garanties du module

Chaque règle, skill, commande et agent de ce dossier sert ces cinq garanties. Où le code s'en écarte, ce fichier le dit et nomme la phase de #184 qui traite l'écart. Un écart nouveau, qu'aucune phase ne traite, devient sa propre issue ou entre dans #184 avant d'être listé ici. Les autres fichiers peuvent détailler un écart listé ici, jamais en ajouter un qui n'y est pas.

Une note d'écart décrit le comportement actuel. La PR d'une phase retire ou réécrit, dans la même PR, chaque note qu'elle résout : ici, dans `README.md`, `docs/guides/usage.md`, `rules/`, `skills/`, `agents/` et `commands/` (`grep -rn "Phase <N>" .claude README.md docs`). Une note qui décrit un comportement corrigé fait échouer la review de la phase.

- **Correctness : un résultat invalide n'est jamais lu comme un succès.**
  - `default(Result<T>)`, comme `new Result<T>()`, n'est ni succès ni échec : `Value`, `Error`, `Map`, `Bind`, `Match` et `Ensure` lèvent `ResultException`, `TryGetValue` retourne `false`, `Tap` ne fait rien, `GetValueOrDefault(x)` retourne `x`. Sur `default(Result)`, `Error`, `Match`, `Map`, `Bind` et `Ensure` lèvent aussi `ResultException`, `MapError` et `Compensate` le retournent sans appeler leur délégué, comme sur `default(Result<T>)`. `CombineAll`, `Partition`, `Successes` et `Failures` lèvent `ResultException` sur un résultat `default` ; `Combine` (tuples compris) et `Traverse` sur un `default` qu'ils atteignent, avant le premier échec.
  - La conversion implicite de `T` vers `Result<T>` refuse un `Result` ou un `Result<T>` comme valeur (`ArgumentException`), possible quand `T` est `object`. `ResultJsonConverter<T>.Read` refuse (`JsonException`) un contenu qui ne décrit pas un résultat valide : succès sans `value`, avec `"value": null` ou avec `error`, échec sans `error` ou avec `value`, `isSuccess` en double, `code` vide. Un mapper de `Try` / `TryAsync` qui retourne `null` lève `ArgumentNullException`. Les filtres d'`ErrorCollection` retournent une liste, jamais une collection vide.
  - `Success(null)` lève `ArgumentNullException`, et un délégué de `Try` / `TryAsync` qui retourne `null` aussi, hors de leur `catch` (ADR-RESULT-001 D6) : l'absence est un échec (`Error.NotFound`), pas une valeur nulle. Une erreur convertie implicitement en `Result<T>` ou `Result` donne un échec (D4).
  - `Error.Category` vaut `Technical` quand elle n'est pas surchargée (ADR-RESULT-001 D5) ; l'analyseur `MKR002` (`MicroKit.Result.Analyzers`) le signale sur une sous-classe concrète qui l'oublie. `MKR001` signale un `Result` retourné puis ignoré. Lire `.Value` sans vérification préalable n'a pas d'analyseur : la vérification dans une méthode d'aide échappe à une analyse limitée à une méthode (#184 Phase 6) ; la review le bloque.
- **Security : les détails internes n'atteignent jamais un client par défaut.**
  - `Error.Message` atteint le client : il reste sans danger pour lui. Les détails internes vont dans le log, pas dans `Metadata` : ADR-RESULT-001 D1 en fait un contexte en mémoire, jamais envoyé au client par défaut : ni `ProblemDetails` ni les convertisseurs JSON, celui du `Result` non générique compris, ne l'écrivent sans `includeMetadata: true`.
  - `Try` / `TryAsync` sans mapper construisent une `ExceptionError` : code `ErrorCode.Exception`, message générique (`"An unexpected error occurred."`), aucune métadonnée ; l'exception reste en mémoire (`ExceptionError.Exception`, `[JsonIgnore]`) pour le log.
  - Le `Result` non générique porte son convertisseur JSON (`[JsonConverter]`, sans réflexion) : retourné tel quel par un endpoint, il répond `{"isSuccess":…}` sans `metadata`, convertisseur enregistré ou non. Les convertisseurs n'écrivent `Metadata` que créés avec `includeMetadata: true`, pour un stockage en mémoire (ADR-RESULT-001 D12).
- **Predictability : le synchrone et l'asynchrone se comportent de la même façon.**
  - `Tap`, `TapError` et `Finally` donnés avec un délégué qui retourne une `Task`, lambda async compris, se lient à une surcharge qui attend l'effet et retourne un `ValueTask<Result<T>>` (ou `ValueTask<Result>`), sur `Result<T>`, `Result`, `Task<Result<T>>` et `ValueTask<Result<T>>`. Une `Action` garde la surcharge synchrone.
  - `Try` et `TryAsync` sans mapper relancent `OperationCanceledException`. Avec un mapper, le mapper reçoit toute exception et décide, et `DatabaseExceptionMapper` (`rules/csharp-style.md`) en fait une erreur : `Try` et `TryAsync` ne servent pas un appel annulable, et leur lambda ne capture pas de `CancellationToken`.
  - Les extensions async et `TryAsync` valident leurs arguments à l'appel, avant tout `await`, la tâche source comprise.
  - Le `Result` non générique a les opérations de `Result<T>`, sync et async (ADR-RESULT-001 D9), et `Bind` mène d'une valeur à une commande (`Result<T>.Bind(Func<T, Result>)`).
  - Chaque opération async a deux formes (D8) : un délégué qui retourne une `Task`, sans jeton ; et un délégué qui prend le `CancellationToken` et retourne un `ValueTask`, avec `CancellationToken ct = default` en dernier paramètre, transmis au délégué. Un port `ValueTask<Result<T>> GetAsync(Guid id, CancellationToken ct = default)` s'y passe directement : `result.BindAsync((u, ct) => repository.GetAsync(u.Id, ct), ct)`. Ces surcharges existent sur `Result<T>` et `Result` ; sur une tâche, on l'attend d'abord.
  - Limite du langage, pas un écart : un lambda non async qui retourne un `ValueTask` (`u => cache.SetAsync(u)`) se lie à la surcharge `Action` de `Tap`, `TapError` ou `Finally`, et son `ValueTask` est abandonné ; CA2012 le signale, une erreur en Release. On l'écrit `async u => await cache.SetAsync(u)`, ou par la forme à jeton de `TapAsync`. Une surcharge `Func<T, ValueTask>` à côté de `Func<T, Task>` rendrait tout lambda async ambigu (CS0121, ADR-RESULT-001 D8).
- **Interoperability : les préoccupations HTTP restent hors du cœur.**
  - `MicroKit.Result` ne référence pas ASP.NET Core (test `Core_DoesNotReferenceAspNetCore`) ; le mapping HTTP vit dans `MicroKit.Result.AspNetCore`. La catégorie d'une `ErrorCollection` suit une préséance fixe, sans connaître HTTP ni dépendre de l'ordre des erreurs : fautes du système (`Technical`, `External`, `Unavailable`, `Timeout`, `NotSupported`, valeurs non définies), puis `Unauthorized`, `Forbidden`, puis `NotFound`, `Conflict`, `PreconditionFailed`, `TooManyRequests`, puis `Cancelled`, puis `Validation`, `BusinessRule` ; à égalité, l'ordre de déclaration d'`ErrorCategory`. Un test d'intégration vérifie chaque paire dans les deux ordres, et qu'une faute du système répond en 5xx.
  - La doc XML du cœur ne cite aucun statut HTTP (ADR-RESULT-001 D11) : `ErrorCategory` décrit des genres d'échec, et `Cancelled` une annulation autre que celle de l'appelant. La table catégorie → statut est sur `ResultProblemDetailsFactory.ToStatusCode`.
  - Les convertisseurs JSON conservent sur un aller-retour `Severity`, `PropertyName` et les erreurs d'une `ErrorCollection` (et `Metadata` sur option) ; la lecture reconstruit une `ErrorCollection`, une `DeserializedValidationError` ou une `DeserializedError`. Sérialiser un `default` lève `JsonException` avec un message clair.
  - `ToHttpResult`, `ToCreatedHttpResult` et `ToAcceptedHttpResult` retournent des `TypedResults` ; les problem details passent par l'`IProblemDetailsService` de l'hôte ; `type` est la section qu'ASP.NET Core connaît pour le statut (RFC 9110, RFC 4918 pour 422), absente pour 429 et 499 ; `Timeout` répond 504 (`rules/result-patterns.md`).
- **Performance : toute affirmation est mesurée.**
  - `tests/MicroKit.Result.PerformanceTests` est une application BenchmarkDotNet ; `README.md` (Performance) publie ses mesures avec la machine et le SDK, contre ErrorOr et FluentResults. Une affirmation de performance cite une mesure de ce projet ; sinon c'est une hypothèse, présentée comme telle.
  - `ErrorCollection` construit `Message` par `string.Join` sur LINQ, ce que la checklist de `agents/dotnet-reviewer.md` bloque dans un nouveau code : mesuré (`ErrorCollectionBenchmarks`), le meilleur candidat ne gagnait que 18–19 % sur le rail d'échec, sous les 20 % que la Phase 7 exigeait pour changer ce chemin. Il reste tel quel.
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
- Erreurs: noun phrases (`UserNotFoundError`, `PaymentDeclinedError`) ; les erreurs de domaine dérivent de `Error`, un type chacune ; la librairie fournit `ValidationError`, `ErrorCollection`, `ExceptionError` (construite par `Try` sans mapper) et `CategorizedError`, que les factories `Error.NotFound(code, message)`… construisent pour une erreur ponctuelle (ADR-RESULT-001 D3)
- Codes d'erreur: `DOMAIN.ENTITY.ACTION` ex: `AUTH.USER.NOT_FOUND`

### Style C# 2026
- Primary constructors partout où pertinent
- File-scoped namespaces obligatoires
- Nullable Reference Types: `enable`
- `readonly record struct` pour les value objects sans héritage (`ErrorCode`, `Unit`) : ce module déroge ici à la convention racine, `sealed record` pour les value objects
- `sealed record` pour les types référence fermés
- `ValueTask<T>` pour l'async (convention racine) ; son coût face à `Task<T>` est mesuré dans `README.md` (Performance, #184 Phase 7) : plus rapide et plus petit quand l'opération se termine de façon synchrone, jusqu'à 6 % plus lent et 13 % plus gros quand elle cède vraiment. La spec de #184 renvoie hors de l'issue la question de convention que la mesure pose

### Interdictions
- ❌ `throw` dans les flux métier normaux
- ❌ `null` retourné depuis une méthode publique (utiliser `Result` ou `Result<T>`), hors un accès dont le nom dit qu'il peut ne rien trouver : `TryGetMetadata<T>`, `GetValueOrDefault()`
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

Mesuré (`README.md`, Performance, sur la machine qu'il indique) :

1. `Result.Success(value)` et `Result.Failure<T>(error)` n'allouent rien ; `Map` et `Bind` n'allouent rien sur les deux rails avec des lambdas sans capture
2. `ValueTask<T>` évite une allocation quand le résultat est disponible de façon synchrone (0 B contre 80 B pour un port) ; quand l'opération cède vraiment, `Task<T>` est un peu plus rapide et alloue un peu moins

Hypothèse non mesurée : une lambda qui capture, un `Ensure` dont l'erreur est construite à chaque appel, un `Where` ou une requête LINQ allouent à chaque appel.

Fait de conception : `ErrorCollection` est matérialisée à la construction (`ImmutableArray<IError>`, `Message` calculé une fois) ; ne la créer que sur le rail d'échec.

## ✅ Vérification et gates

La vérification a lieu à la fin de chaque étape, pas à chaque édition. Depuis la racine du dépôt :

```bash
dotnet build modules/MicroKit.Result/MicroKit.Result.slnx -c Release
dotnet test  modules/MicroKit.Result/MicroKit.Result.slnx -c Release
dotnet build MicroKit.slnx -c Release -p:WarningsNotAsErrors=NU1902%3BNU1903   # garde : aucun autre module ne casse (#161)
dotnet format modules/MicroKit.Result/MicroKit.Result.slnx --verify-no-changes
```

Chaque bloc C# de `README.md` et de `docs/guides/usage.md` existe dans `samples/MicroKit.Result.Samples/Guide/`, à l'indentation près, ses directives `using` en tête du fichier ; le build Release de ces samples fait tourner les analyseurs `MKR001`/`MKR002`. Aucun test ne le vérifie encore : la review compare les blocs aux samples.

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
- `CHANGELOG.md` à jour en cas de rupture ; `docs/guides/usage.md` pour toute nouvelle API publique, dont chaque bloc C# existe dans `samples/MicroKit.Result.Samples/Guide/` et compile en Release (le `README.md` ne garde qu'un démarrage rapide)
- Aucune version dans un `.csproj` : versions et releases relèvent d'ADR-GLOBAL-002
- Pas de `TODO` sans issue : `// TODO(#184): ...`

## 📦 Packages
- `MicroKit.Result` — core (zéro dépendance) ; inclut les convertisseurs JSON (namespace `MicroKit.Result.Serialization`)
- `MicroKit.Result.AspNetCore` — extensions HTTP/ProblemDetails
- `MicroKit.Result.Analyzers` — analyseurs Roslyn, au build seulement : `MKR001` (résultat ignoré), `MKR002` (catégorie par défaut)

## 🔗 Références
- [ADR-RESULT-001 — le modèle d'erreur](../.claude-context/context/architecture/decisions/ADR-RESULT-001-error-model.md) : rôle de `Code`, `Category`, `Severity`, `Metadata`, factories, conversions, async et parité du `Result` non générique
- [Railway Oriented Programming - Scott Wlaschin](https://fsharpforfunandprofit.com/rop/)
- [Error handling in .NET — Microsoft](https://learn.microsoft.com/dotnet/standard/exceptions/)
- [OneOf library](https://github.com/mcintyre321/OneOf) — inspiration pattern
- [FluentResults](https://github.com/altmann/FluentResults) — inspiration API
