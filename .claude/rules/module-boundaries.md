# Rule: Module Boundaries — MicroKit

## Toujours actif pour toute tâche inter-modules.

## Principe fondamental
> Chaque module doit fonctionner de façon autonome.
> L'intégration entre modules est une valeur ajoutée, pas un prérequis.

## Règles de dépendances

### Ce qui est autorisé
```csharp
// ✅ Dépendance sur les Abstractions d'un autre module (en production)
<PackageReference Include="MicroKit.Result.Abstractions" />
<PackageReference Include="MicroKit.Domain.Abstractions" />

// ✅ Dépendance sur le package complet dans les tests d'intégration
// (tests/MicroKit.MediatR.IntegrationTests/)
<PackageReference Include="MicroKit.Result" />

// ✅ ProjectReference uniquement dans le même module (src → tests, etc.)
<ProjectReference Include="../MicroKit.Result/MicroKit.Result.csproj" />
```

### Ce qui est interdit
```csharp
// ❌ ProjectReference vers un autre module en production
// (dans src/MicroKit.MediatR/)
<ProjectReference Include="../../MicroKit.Result/src/MicroKit.Result/MicroKit.Result.csproj" />
// ✅ Utiliser PackageReference à la place

// ❌ Dépendance sur l'implémentation d'un autre module (src/ non-Abstractions)
<PackageReference Include="MicroKit.Result" />
// dans src/MicroKit.MediatR.Abstractions/ → uniquement MicroKit.Result.Abstractions

// ❌ Dépendance sur un module non autorisé dans le graphe
// MicroKit.Result NE PEUT PAS dépendre de MicroKit.MediatR
// (Result est plus bas dans le graphe)
```

## Règles des Abstractions

```
MicroKit.[Module].Abstractions/
  ✅ Interfaces, contrats, types de valeur
  ✅ Peut dépendre d'autres .Abstractions packages
  ❌ Pas d'implémentation (classes concrètes sauf records/structs immuables)
  ❌ Pas de dépendance sur MicroKit.[Module] (son propre core)
  ❌ Pas de dépendance sur des packages tiers avec implémentation
     (Microsoft.Extensions.* ok si ce sont des abstractions)
```

## Règle de l'interface adapter

Quand un module A veut utiliser une fonctionnalité du module B sans dépendance directe :

```csharp
// ✅ Pattern adapter dans MicroKit.MediatR (pour éviter de dépendre de Result core)
// MicroKit.MediatR.Abstractions définit son propre IResult<T>
// MicroKit.MediatR.Result (package optionnel) fait le pont avec MicroKit.Result

// Exemple :
// MicroKit.Messaging n'a pas besoin de MicroKit.MediatR
// Mais MicroKit.Messaging.MediatR (package optionnel) peut dépendre des deux
```

## Graphe de dépendances — rappel visuel

```
Niveau 0 (fondations — aucune dépendance MicroKit) :
  ├── MicroKit.Domain
  └── MicroKit.Result

Niveau 1 (dépendent de Result ou Domain) :
  ├── MicroKit.Logging         → Result
  ├── MicroKit.Caching         → Result
  └── MicroKit.Auth            → Result, Domain

Niveau 2 (dépendent du niveau 1) :
  ├── MicroKit.Observability   → Result, Logging
  ├── MicroKit.Persistence     → Result, Domain
  └── MicroKit.MediatR         → Result, Domain

Niveau 3 (dépendent du niveau 2) :
  ├── MicroKit.Http            → Result, Observability
  ├── MicroKit.Messaging       → Result, Domain, Persistence
  └── MicroKit.Tenancy    → Result, Auth, Persistence
```

## Détection des violations

Signaux d'alerte déclenchant une review obligatoire :
```
🔴 ProjectReference vers modules/ depuis src/ d'un autre module
🔴 using MicroKit.[ModuleX] dans src/ d'un module de niveau inférieur dans le graphe
🔴 Nouvelle entrée dans Directory.Packages.props référencée uniquement dans .Abstractions
   avec un package qui contient du code d'implémentation
🟡 Package MicroKit en pre-release utilisé dans un module stable
🟡 Version pin explicite sur un package MicroKit (doit être flexible ^x.y)
```
