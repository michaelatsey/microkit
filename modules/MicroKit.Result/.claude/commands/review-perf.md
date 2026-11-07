# Command: /review-perf

## Usage
```
/review-perf <FilePath|Directory>
```

## Description
Repère dans un fichier ou répertoire les risques de performance et d'allocations.
Utilise l'agent `dotnet-reviewer` avec un focus spécifique performance.

Lire le code ne mesure rien : chaque constat est un risque non mesuré, sans chiffre ni estimation d'impact, tant qu'un benchmark BenchmarkDotNet ne le confirme pas. #184 Phase 7 publie les benchmarks du module.

## Ce qui est analysé

### Allocations
- `new List<T>()` dans des méthodes appelées souvent → ne créer que sur le rail d'échec, ou partir de `ImmutableArray<T>.Empty`
- `string.Format` / interpolation dans des hot paths → ne construire le message que sur le rail d'échec
- Lambdas qui capturent (`this`, un paramètre, un method group d'instance) → risque à mesurer. Capturer un paramètre plutôt que `this` n'évite rien : `Map` n'a pas de surcharge à état, le paramètre est capturé à son tour. Seule une lambda `static`, quand le mapping n'a besoin d'aucun état, supprime la capture (`skills/dotnet-performance/SKILL.md`)
- `.ToList()` / `.ToArray()` intermédiaires inutiles
- Boxing de value types (struct → object, struct → interface)

### Async overhead
- `async` method sans vrai `await` → à signaler. Retirer `async` change le moment où une exception sort : à l'appel plutôt qu'à l'`await`. Le fix ne vaut que si le comportement de chaque chemin d'exception reste celui attendu
- `await Task.FromResult(x)` → retirer le wrapper
- `ConfigureAwait(false)` manquant dans la librairie
- `ValueTask` bloqué ou attendu deux fois (CA2012). Le choix `ValueTask<T>` lui-même suit la convention racine ; son gain face à `Task<T>` n'est pas mesuré, la Phase 7 le mesure

### LINQ
- `.Where(p).First()` → `.First(p)`, même comportement quand rien ne correspond (`FirstOrDefault` retournerait `default` au lieu de lever)
- `.Select(f).Where(p)` → `.Where(…).Select(f)` seulement en réécrivant le prédicat sur la valeur d'origine : `p` porte sur la valeur projetée, et l'inverser tel quel change le résultat
- LINQ dans les constructeurs ou propriétés → matérialiser une fois

### JIT hints
Ne jamais les proposer sans mesure : Microsoft Learn (*MethodImplOptions*) demande de toujours mesurer leur effet.
- `AggressiveInlining` : un usage inutile peut dégrader les performances
- `AggressiveOptimization` : « rarely appropriate », la méthode perd les optimisations du tiered compilation (dynamic PGO)

## Output format
```
📊 Performance Review: {File}

🔴 Risques d'allocations (non mesurés) :
  Ligne 42: new List<IError>() dans Map(), sur le chemin succès
  Fix: ne créer la liste que sur le rail d'échec

🟡 Async overhead (non mesuré) :
  Ligne 78: async sans await réel
  Fix possible : retirer async, retourner ValueTask.FromResult(result) ; une exception sortirait alors à l'appel

🧪 À mesurer :
  Benchmark à écrire pour confirmer l'effet de chaque fix proposé
```
