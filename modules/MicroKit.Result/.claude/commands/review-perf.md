# Command: /review-perf

## Usage
```
/review-perf <FilePath|Directory> [--threshold allocations=0,ns=10]
```

## Description
Analyse un fichier ou répertoire pour les problèmes de performance et d'allocations.
Utilise l'agent `dotnet-reviewer` avec un focus spécifique performance.

## Ce qui est analysé

### Allocations
- `new List<T>()` dans des méthodes appelées souvent → utiliser `stackalloc` ou pooling
- `string.Format` / interpolation dans des hot paths → `string.Create`
- Closures qui capturent `this` inutilement → extraire en méthode statique avec param
- `.ToList()` / `.ToArray()` intermédiaires inutiles
- Boxing de value types (struct → object, struct → interface)

### Async overhead
- `async` method sans vrai `await` → retirer `async`, retourner `ValueTask.FromResult`
- `await Task.FromResult(x)` → retirer le wrapper
- `ConfigureAwait(false)` manquant dans la librairie
- `Task` là où `ValueTask` suffira

### LINQ
- `.Where().First()` → `.FirstOrDefault(predicate)`
- `.Select().Where()` → `.Where().Select()` (filtre avant transformation)
- LINQ dans les constructeurs ou propriétés → matérialiser une fois

### JIT hints
- Méthodes courtes non inlinées → `[MethodImpl(MethodImplOptions.AggressiveInlining)]`
- Hot path avec branches → `[MethodImpl(MethodImplOptions.AggressiveOptimization)]`

## Output format
```
📊 Performance Review: {File}

🔴 Allocations détectées:
  Ligne 42: new List<IError>() dans Map() — appelé souvent
  Fix: utiliser ImmutableArray<IError>.Empty comme default

🟡 Async overhead:
  Ligne 78: async sans await réel
  Fix: retirer async, retourner ValueTask.FromResult(result)

✅ Bon:
  readonly record struct Unit — 0 allocation
  ThrowHelper.ThrowIfNull — inlinable par JIT

📈 Estimation d'impact:
  Map() hot path: -1 allocation/call (-8 bytes GC pressure)
  Async overhead: -2 state machine allocations
```
