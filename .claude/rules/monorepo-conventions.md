# Rule: Monorepo Conventions — MicroKit

## Toujours actif pour toute tâche dans le monorepo.

## Structure des fichiers

### Nommage obligatoire
```
modules/MicroKit.[Module]/                    ← PascalCase, sans tirets
  src/MicroKit.[Module].Abstractions/         ← contrats uniquement
  src/MicroKit.[Module]/                      ← implémentation core
  src/MicroKit.[Module].[Provider]/           ← intégration optionnelle
  tests/MicroKit.[Module].UnitTests/
  tests/MicroKit.[Module].IntegrationTests/
  tests/MicroKit.[Module].ArchitectureTests/
  tests/MicroKit.[Module].PerformanceTests/
```

### Namespaces
```csharp
// ✅ Namespace racine = nom du package
namespace MicroKit.Result;
namespace MicroKit.MediatR.Behaviors;
namespace MicroKit.Persistence.EntityFramework;

// ❌ Pas de .Core dans le namespace racine
namespace MicroKit.Result.Core; // ❌
```

### Nommage des packages NuGet
```
MicroKit.[Module]                 ← core
MicroKit.[Module].Abstractions    ← contrats purs
MicroKit.[Module].[Provider]      ← provider/intégration
MicroKit.[Module].Testing         ← helpers de test
MicroKit.[Module].Analyzers       ← Roslyn analyzers
```

## Conventional Commits (obligatoire)

```
<type>(<scope>): <description>

Types : feat, fix, perf, refactor, test, docs, chore, build, ci
Scope : nom du module en minuscule (result, mediatr, domain, caching...)
        ou "monorepo" pour les changements transversaux

Exemples :
  feat(result): add EnsureAsync overload with factory
  fix(mediatr): correct pipeline order when behaviors share same order value
  perf(caching): reduce allocations in CachingBehavior hot path
  docs(domain): add aggregate root usage guide
  chore(monorepo): update Directory.Packages.props to net9.0 packages
  ci(result): add performance test job to CI workflow
  feat(domain)!: rename IDomainEvent to IEvent (BREAKING CHANGE)
```

### Breaking changes
```
Ajouter un `!` après le scope : feat(result)!:
OU ajouter un footer : BREAKING CHANGE: description
Les deux sont requis pour un MAJOR bump automatique via Nerdbank
```

## Branches

```
main              ← protégée, toujours stable, release uniquement via tag
dev               ← intégration — PRs des features vont ici
feature/<scope>/<description>   ← feature branch
  feature/result/ensure-async
  feature/mediatr/streaming-support
release/<module>/<version>      ← préparation release
  release/result/1.2.0
fix/<scope>/<description>        ← bugfix
  fix/mediatr/pipeline-order
```

## Pull Requests

### Template obligatoire
```
## Changements
[Description des changements]

## Module(s) impacté(s)
- [ ] MicroKit.Result
- [ ] MicroKit.MediatR
- [ ] Autre : ___

## Type de changement
- [ ] feat (nouvelle fonctionnalité)
- [ ] fix (correctif)
- [ ] perf (performance)
- [ ] breaking change

## Checklist
- [ ] Tests ajoutés/mis à jour
- [ ] XML docs à jour
- [ ] CHANGELOG.md mis à jour
- [ ] Pas de breaking change non documentée
```

### Labels de PR
```
breaking-change     → review obligatoire de l'orchestrateur
cross-module        → impacte plusieurs modules
performance         → inclut benchmarks
needs-docs          → documentation manquante
release-blocker     → bloque la prochaine release
```

## Fichiers sacrés (jamais modifiés sans discussion)

```
build/Directory.Build.props      → propriétés MSBuild globales
build/Directory.Packages.props   → versions NuGet centralisées
.editorconfig                    → style de code partagé
global.json                      → version SDK .NET fixée
.github/workflows/release.yml   → workflow de publication NuGet
```
