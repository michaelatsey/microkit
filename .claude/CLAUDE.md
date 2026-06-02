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
| **MicroKit.Result** | `modules/MicroKit.Result/` | `modules/MicroKit.Result/.claude/` | ✅ Released 1.0.0-preview.1 |
| **MicroKit.Domain** | `modules/MicroKit.Domain/` | `modules/MicroKit.Domain/.claude/` | ✅ Released 1.0.0-preview.4 |
| **MicroKit.Logging** | `modules/MicroKit.Logging/` | `modules/MicroKit.Logging/.claude/` | ✅ Released 1.0.0-preview.1 |
| **MicroKit.MediatR** | `modules/MicroKit.MediatR/` | `modules/MicroKit.MediatR/.claude/` | ✅ Released 1.0.0-preview.1 |
| **MicroKit.Persistence** | `modules/MicroKit.Persistence/` | `modules/MicroKit.Persistence/.claude/` | ✅ Released 1.0.0-preview.1 |
| **MicroKit.Messaging** | `modules/MicroKit.Messaging/` | `modules/MicroKit.Messaging/.claude/` | 📋 Planifié |
| **MicroKit.Caching** | `modules/MicroKit.Caching/` | `modules/MicroKit.Caching/.claude/` | 📋 Planifié |
| **MicroKit.Http** | `modules/MicroKit.Http/` | `modules/MicroKit.Http/.claude/` | 📋 Planifié |
| **MicroKit.Auth** | `modules/MicroKit.Auth/` | `modules/MicroKit.Auth/.claude/` | 📋 Planifié |
| **MicroKit.Observability** | `modules/MicroKit.Observability/` | `modules/MicroKit.Observability/.claude/` | 📋 Planifié |
| **MicroKit.Multitenancy** | `modules/MicroKit.Multitenancy/` | `modules/MicroKit.Multitenancy/.claude/` | 🚧 Bootstrapped |

### Règle de navigation pour Claude Code
```
Tâche sur un module spécifique  → lire modules/MicroKit.[X]/.claude/CLAUDE.md EN PREMIER
Tâche transversale (build, CI)  → lire ce fichier + .claude/rules/monorepo-conventions.md
Nouvelle feature cross-module   → lire ce fichier + les .claude/ des modules concernés
Ajout d'un nouveau module       → lire .claude/skills/new-module-bootstrap.md
Écriture de tests               → lire .claude/rules/testing-libraries.md (Shouldly obligatoire)
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
├── .claude-context/
│   └── sessions/                     ← résumés de sessions (lire le plus récent)
│
├── .github/
│   ├── workflows/
│   │   ├── ci-domain.yml             ← CI MicroKit.Domain
│   │   ├── ci-result.yml             ← CI MicroKit.Result
│   │   ├── ci-logging.yml            ← CI MicroKit.Logging
│   │   ├── ci-mediatr.yml            ← CI MicroKit.MediatR
│   │   ├── release-domain.yml
│   │   ├── release-result.yml
│   │   ├── release-logging.yml
│   │   └── release-mediatr.yml
│   ├── CODEOWNERS
│   └── pull_request_template.md
│
├── build/
│   ├── Directory.Build.props         ← props communs à tous les projets
│   ├── Directory.Build.targets       ← targets communs
│   ├── Directory.Packages.props      ← NuGet Central Package Management
│   └── version.json                  ← Nerdbank.GitVersioning config
│
├── modules/
│   ├── MicroKit.Result/
│   ├── MicroKit.Domain/
│   ├── MicroKit.Logging/
│   ├── MicroKit.MediatR/
│   ├── MicroKit.Persistence/
│   └── ...
│
├── .editorconfig
├── .gitignore
├── global.json                       ← SDK .NET version fixée
├── MicroKit.slnx                     ← solution racine (tous modules)
└── README.md
```

### Structure interne de chaque module
```
modules/MicroKit.[Module]/
├── .claude/                          ← cerveau du module (indépendant)
├── .claude-context/                  ← standards, templates, ADRs (chargés par les agents)
│   ├── standards/
│   ├── templates/
│   └── context/
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
├── README.md
└── MicroKit.[Module].slnx
```

---

## 📦 Dépendances inter-modules

### Graphe de dépendances (autorisées)
```
MicroKit.Domain          ← aucune dépendance sur les autres modules
MicroKit.Result          ← aucune dépendance sur les autres modules
MicroKit.Logging         ← ADR-006: ne dépend PAS de Result (permanent)
MicroKit.Observability   ← peut dépendre de Result, Logging
MicroKit.Auth            ← peut dépendre de Result, Domain
MicroKit.Caching         ← peut dépendre de Result
MicroKit.Persistence     ← peut dépendre de Result, Domain
MicroKit.Messaging       ← peut dépendre de Result, Domain, Persistence (outbox)
MicroKit.Http            ← peut dépendre de Result, Observability
MicroKit.MediatR         ← peut dépendre de Result, Domain, Logging.Abstractions
MicroKit.Multitenancy    ← peut dépendre de Result, Auth, Persistence
```

### Règle de dépendance
> Un module **Abstractions** ne dépend **jamais** d'un autre module non-Abstractions.
> Les dépendances circulaires entre modules sont **interdites**.
> Toute nouvelle dépendance inter-module nécessite une mise à jour de ce graphe.

---

## 🔢 Versioning — Nerdbank.GitVersioning

Chaque module est versionné **indépendamment** via `version.json` dans son répertoire.

### Convention de tags Git pour les releases
```
result-v1.0.0-preview.1   → release MicroKit.Result
domain-v1.0.0-preview.1   → release MicroKit.Domain
logging-v1.0.0-preview.1  → release MicroKit.Logging
mediatr-v1.0.0-preview.1  → release MicroKit.MediatR
persistence-v1.0.0-preview.1 → release MicroKit.Persistence
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

```xml
Nullable: enable
ImplicitUsings: enable
LangVersion: latest
TreatWarningsAsErrors: true (Release uniquement)
AnalysisLevel: latest-recommended
NuGet: Central Package Management via Directory.Packages.props
```

---

## ✅ Conventions globales (valables pour tous les modules)

### Règles non négociables (tous modules)
- `sealed record` pour erreurs/VO/events | `sealed class` pour handlers/behaviors
- `ValueTask<T>` async | `ConfigureAwait(false)` dans les libs
- `CancellationToken ct = default` toujours en dernier
- `Console.WriteLine` interdit → `ILogger<T>`
- Zéro dépendance circulaire | `.Abstractions` → uniquement autres `.Abstractions`
- Tests : `GenerateDocumentationFile=false` + `NoWarn CS1591;CA1707`
- CPM : toutes les versions dans `Directory.Packages.props` racine
- **`Shouldly` (MIT) obligatoire** — FluentAssertions INTERDIT (licence commerciale Xceed v8+)
- **`NSubstitute`** pour les mocks
- **`NetArchTest`** pour les tests d'architecture
- `.claude/` complet AVANT toute implémentation

### Conventions de commit
```
feat(result): add EnsureAsync overload
fix(mediatr): correct pipeline order with custom behaviors
chore(build): update Directory.Packages.props
docs(domain): add aggregate root design guide
```

### Nommage des packages NuGet publiés
```
MicroKit.Result                                        ✅ 1.0.0-preview.1
MicroKit.Result.AspNetCore                             ✅ 1.0.0-preview.1
MicroKit.Domain                                        ✅ 1.0.0-preview.4
MicroKit.Logging                                       ✅ 1.0.0-preview.1
MicroKit.Logging.Abstractions                          ✅ 1.0.0-preview.1
MicroKit.Logging.OpenTelemetry                         ✅ 1.0.0-preview.1
MicroKit.Logging.AspNetCore                            ✅ 1.0.0-preview.1
MicroKit.Logging.Diagnostics                           ✅ 1.0.0-preview.1
MicroKit.Logging.Analyzers                             ✅ 1.0.0-preview.1
MicroKit.Logging.Generators                            ✅ 1.0.0-preview.1
MicroKit.MediatR                                       ✅ 1.0.0-preview.1
MicroKit.MediatR.Abstractions                          ✅ 1.0.0-preview.1
MicroKit.MediatR.Behaviors                             ✅ 1.0.0-preview.1
MicroKit.MediatR.Testing                               ✅ 1.0.0-preview.1
MicroKit.Persistence.Abstractions                      ✅ 1.0.0-preview.1
MicroKit.Persistence                                   ✅ 1.0.0-preview.1
MicroKit.Persistence.EntityFrameworkCore               ✅ 1.0.0-preview.1
MicroKit.Persistence.EntityFrameworkCore.PostgreSql    ✅ 1.0.0-preview.1
MicroKit.Persistence.EntityFrameworkCore.SqlServer     ✅ 1.0.0-preview.1
MicroKit.Persistence.Specifications                    ✅ 1.0.0-preview.1
MicroKit.Persistence.Testing                           ✅ 1.0.0-preview.1
MicroKit.Persistence.Analyzers                         ✅ 1.0.0-preview.1
MicroKit.Messaging                                     📋 planifié
MicroKit.Messaging.AzureServiceBus                     📋 planifié
MicroKit.Messaging.RabbitMQ                            📋 planifié
```

## Sessions
Lire le fichier de session le plus récent dans `.claude-context/sessions/`
avant de commencer tout travail.
