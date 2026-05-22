# Command: /new-module

## Usage
```
/new-module <ModuleName> [--depends-on <Module1,Module2>] [--providers <Provider1,Provider2>] [--analyzers] [--generators]
```

## Description
Bootstrappe un nouveau module MicroKit complet :
structure de répertoires, fichiers de projet, `.claude/` initial, version.json,
workflow CI, et enregistrement dans le registre racine.

## Exemples
```
/new-module Caching --depends-on Result
/new-module Messaging --depends-on Result,Domain,Persistence --providers AzureServiceBus,RabbitMQ
/new-module Persistence --depends-on Result,Domain --providers EntityFramework,Dapper --analyzers
```

## Ce qui est créé (structure uniquement — pas de code source)

```
modules/MicroKit.[ModuleName]/
├── .claude/
│   ├── CLAUDE.md                    ← à remplir (template fourni)
│   ├── settings.json                ← pré-configuré
│   ├── agents/                      ← vide, à créer
│   ├── commands/                    ← vide, à créer
│   ├── hooks/                       ← vide, à créer
│   ├── rules/                       ← vide, à créer
│   └── skills/                      ← vide, à créer
├── docs/
│   ├── guides/
│   ├── architecture/
│   └── README.md
├── src/
│   ├── MicroKit.[ModuleName].Abstractions/
│   │   └── MicroKit.[ModuleName].Abstractions.csproj
│   ├── MicroKit.[ModuleName]/
│   │   └── MicroKit.[ModuleName].csproj
│   └── (MicroKit.[ModuleName].[Provider]/ pour chaque --providers)
├── tests/
│   ├── MicroKit.[ModuleName].UnitTests/
│   ├── MicroKit.[ModuleName].IntegrationTests/
│   ├── MicroKit.[ModuleName].ArchitectureTests/
│   └── MicroKit.[ModuleName].PerformanceTests/
├── samples/
├── benchmarks/
├── build/
├── version.json                     ← Nerdbank config
├── README.md
└── MicroKit.[ModuleName].slnx
```

## Étapes post-création (checklist affichée à l'utilisateur)

```
✅ Structure créée : modules/MicroKit.[ModuleName]/

📋 Actions manuelles requises :
  1. Remplir modules/MicroKit.[ModuleName]/.claude/CLAUDE.md
     (utilise /bootstrap-module-claude [ModuleName] pour le générer)
  
  2. Vérifier la dépendance dans .claude/CLAUDE.md graphe
     Dépendances déclarées : [depends-on]
  
  3. Ajouter le module à MicroKit.slnx (solution racine)
     → dotnet sln MicroKit.slnx add modules/MicroKit.[ModuleName]/MicroKit.[ModuleName].slnx
  
  4. Créer le workflow CI GitHub Actions
     → copier .github/workflows/ci-result.yml → ci-[module-kebab].yml
     → adapter les paths de changeset detection
  
  5. Enregistrer dans .claude/settings.json moduleRegistry
     (mise à jour automatique si confirmée)
```

## Validation avant création
- [ ] Nom du module ne duplique pas un module existant
- [ ] Dépendances déclarées existent dans moduleRegistry
- [ ] Pas de dépendance circulaire introduite
- [ ] Nom suit la convention PascalCase sans tirets
