# MicroKit — Monorepo Root Brain

## 🎯 Vision
MicroKit est un écosystème de librairies .NET 10+ modulaires, opinionées et production-ready.
Chaque module est autonome, versionnée indépendamment, publiée sur NuGet, et conçue pour
s'assembler sans friction dans une architecture hexagonale / DDD / CQRS / microservices.

> **Principe fondamental :** chaque module doit valoir seul. L'intégration est un bonus, pas un prérequis.

---

## 🗺️ Navigation — Où trouver le contexte

Quand tu travailles sur un module spécifique, **charge toujours son `.claude/CLAUDE.md`** en priorité.
Ce fichier racine donne la vision globale et les conventions transversales.

### Carte des modules

| Module | Chemin | .claude/ | Statut |
|--------|--------|----------|--------|
| **MicroKit.Result** | `modules/MicroKit.Result/` | `modules/MicroKit.Result/.claude/` | ✅ Actif |
| **MicroKit.MediatR** | `modules/MicroKit.MediatR/` | `modules/MicroKit.MediatR/.claude/` | ✅ Actif |
| **MicroKit.Domain** | `modules/MicroKit.Domain/` | `modules/MicroKit.Domain/.claude/` | ✅ Actif |
| **MicroKit.Messaging** | `modules/MicroKit.Messaging/` | `modules/MicroKit.Messaging/.claude/` | 📋 Planifié |
| **MicroKit.Persistence** | `modules/MicroKit.Persistence/` | `modules/MicroKit.Persistence/.claude/` | 📋 Planifié |
| **MicroKit.Caching** | `modules/MicroKit.Caching/` | `modules/MicroKit.Caching/.claude/` | 📋 Planifié |
| **MicroKit.Http** | `modules/MicroKit.Http/` | `modules/MicroKit.Http/.claude/` | 📋 Planifié |
| **MicroKit.Auth** | `modules/MicroKit.Auth/` | `modules/MicroKit.Auth/.claude/` | 📋 Planifié |
| **MicroKit.Observability** | `modules/MicroKit.Observability/` | `modules/MicroKit.Observability/.claude/` | 📋 Planifié |
| **MicroKit.Logging** | `modules/MicroKit.Logging/` | `modules/MicroKit.Logging/.claude/` | 📋 Planifié |
| **MicroKit.Multitenancy** | `modules/MicroKit.Multitenancy/` | `modules/MicroKit.Multitenancy/.claude/` | 📋 Planifié |

### Règle de navigation pour Claude Code
```
Tâche sur un module spécifique  → lire modules/MicroKit.[X]/.claude/CLAUDE.md EN PREMIER
Tâche transversale (build, CI)  → lire ce fichier + .claude/rules/monorepo-conventions.md
Nouvelle feature cross-module   → lire ce fichier + les .claude/ des modules concernés
Ajout d'un nouveau module       → lire .claude/skills/new-module-bootstrap.md
```

---

## 🏛️ Architecture du Monorepo

### Structure physique
```
MicroKit/
├── .claude/                          ← cerveau global (conventions transversales)
│   ├── CLAUDE.md                     ← ce fichier
│   ├── agents/                       ← agents globaux (release, cross-module)
│   ├── commands/                     ← commandes globales (/new-module, /release, etc.)
│   ├── hooks/                        ← hooks monorepo (pre-commit global, etc.)
│   ├── rules/                        ← règles transversales
│   └── skills/                       ← skills globaux (build, versioning, CI)
│
├── .github/
│   ├── workflows/
│   │   ├── ci.yml                    ← build + test sur PR (tous modules)
│   │   ├── release.yml               ← publish NuGet sur tag
│   │   ├── ci-result.yml             ← CI spécifique MicroKit.Result
│   │   └── ci-[module].yml           ← CI par module (changeset detection)
│   ├── CODEOWNERS
│   └── pull_request_template.md
│
├── build/
│   ├── Directory.Build.props         ← props communs à tous les projets
│   ├── Directory.Build.targets       ← targets communs
│   ├── Directory.Packages.props      ← NuGet Central Package Management
│   └── version.json                  ← Nerdbank.GitVersioning config
│
├── eng/
│   ├── scripts/
│   │   ├── build-all.ps1
│   │   ├── test-all.ps1
│   │   └── pack-all.ps1
│   └── analyzers/
│       └── MicroKit.Analyzers.props  ← analyzers partagés (Roslynator, etc.)
│
├── modules/
│   ├── MicroKit.Result/              ← voir structure interne ci-dessous
│   ├── MicroKit.MediatR/
│   └── ...
│
├── docs/
│   ├── architecture/
│   │   ├── overview.md               ← vue d'ensemble de l'écosystème
│   │   ├── module-dependencies.md    ← graphe de dépendances inter-modules
│   │   └── design-decisions/         ← ADRs (Architecture Decision Records)
│   └── guides/
│       ├── getting-started.md
│       └── contributing.md
│
├── .editorconfig                     ← style partagé tous modules
├── .gitignore
├── global.json                       ← SDK .NET version fixée
├── MicroKit.slnx                     ← solution racine (tous modules)
└── README.md
```

### Structure interne de chaque module
```
modules/MicroKit.[Module]/
├── .claude/                          ← cerveau du module (indépendant)
├── docs/
│   ├── guides/
│   ├── architecture/
│   └── README.md
├── src/
│   ├── MicroKit.[Module].Abstractions/   ← contrats purs, zéro dépendance tierce
│   ├── MicroKit.[Module]/                ← implémentation core
│   ├── MicroKit.[Module].[Provider]/     ← intégrations optionnelles
│   ├── MicroKit.[Module].Analyzers/      ← Roslyn analyzers (optionnel)
│   └── MicroKit.[Module].Generators/     ← source generators (optionnel)
├── tests/
│   ├── MicroKit.[Module].UnitTests/
│   ├── MicroKit.[Module].IntegrationTests/
│   ├── MicroKit.[Module].ArchitectureTests/
│   └── MicroKit.[Module].PerformanceTests/
├── samples/
├── benchmarks/
├── build/                            ← props spécifiques au module si nécessaire
├── README.md
└── MicroKit.[Module].slnx
```

---

## 📦 Dépendances inter-modules

### Graphe de dépendances (autorisées)
```
MicroKit.Domain          ← aucune dépendance sur les autres modules
MicroKit.Result          ← aucune dépendance sur les autres modules
MicroKit.Logging         ← peut dépendre de Result
MicroKit.Observability   ← peut dépendre de Result, Logging
MicroKit.Auth            ← peut dépendre de Result, Domain
MicroKit.Caching         ← peut dépendre de Result
MicroKit.Persistence     ← peut dépendre de Result, Domain
MicroKit.Messaging       ← peut dépendre de Result, Domain, Persistence (outbox)
MicroKit.Http            ← peut dépendre de Result, Observability
MicroKit.MediatR         ← peut dépendre de Result, Domain
MicroKit.Multitenancy    ← peut dépendre de Result, Auth, Persistence
```

### Règle de dépendance
> Un module **Abstractions** ne dépend **jamais** d'un autre module non-Abstractions.
> Les dépendances circulaires entre modules sont **interdites**.
> Toute nouvelle dépendance inter-module nécessite une mise à jour de ce graphe.

---

## 🔢 Versioning — Nerdbank.GitVersioning

Chaque module est versionné **indépendamment** via `version.json` dans son répertoire.

```json
// modules/MicroKit.Result/version.json
{
  "version": "1.0",
  "publicReleaseRefSpec": ["^refs/heads/main$", "^refs/tags/result-v\\d+\\.\\d+"],
  "cloudBuild": { "setVersionVariables": true }
}
```

### Convention de tags Git pour les releases
```
result-v1.2.0          → release MicroKit.Result 1.2.0
mediatr-v1.0.0         → release MicroKit.MediatR 1.0.0
domain-v1.0.0-beta.1   → pre-release MicroKit.Domain
```

### Branches
```
main              ← toujours stable, protégée
dev               ← intégration continue
feature/*         ← features (scope: result/fix-map, mediatr/add-streaming)
release/*         ← préparation de release (release/result-1.2)
```

---

## 🏗️ Build partagé — Directory.Build.props

Propriétés appliquées à **tous** les projets du monorepo :
```xml
<!-- Voir build/Directory.Build.props pour le détail -->
Nullable: enable
ImplicitUsings: enable
LangVersion: latest
TreatWarningsAsErrors: true (Release uniquement)
AnalysisLevel: latest-recommended
NuGet: Central Package Management via Directory.Packages.props
```

---

## 🤖 CI/CD — GitHub Actions

### Workflow principal (`ci.yml`)
- Déclenché sur : PR vers `main` ou `dev`
- Détecte les modules modifiés (changeset)
- Lance uniquement les tests des modules impactés + leurs dépendants
- Publie les résultats de coverage (Codecov)

### Workflow de release (`release.yml`)
- Déclenché sur : push de tag `[module]-v*`
- Build + test + pack + push NuGet
- Génère les release notes depuis CHANGELOG.md

### Convention de PR
```
feat(result): add EnsureAsync overload
fix(mediatr): correct pipeline order with custom behaviors
chore(build): update Directory.Packages.props
docs(domain): add aggregate root design guide
```

---

## ✅ Conventions globales (valables pour tous les modules)

### Ce qui EST défini ici (global)
- Versioning et tags Git
- Structure des modules (imposée)
- Propriétés MSBuild partagées
- Conventions de nommage des packages NuGet
- Format des PRs et commits (Conventional Commits)
- Politique de dépendances inter-modules

### Ce qui EST défini dans chaque module (local)
- Philosophie et patterns spécifiques au module
- Agents, commands, rules, skills propres
- Décisions d'API et de conception
- Dépendances NuGet spécifiques

### Nommage des packages NuGet publiés
```
MicroKit.Result
MicroKit.Result.AspNetCore
MicroKit.MediatR
MicroKit.MediatR.Behaviors
MicroKit.MediatR.Testing
MicroKit.Domain
MicroKit.Domain.Abstractions
MicroKit.Persistence
MicroKit.Persistence.EntityFramework
MicroKit.Messaging
MicroKit.Messaging.AzureServiceBus
MicroKit.Messaging.RabbitMQ
...
```
