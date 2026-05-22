# Command: /audit-dependencies

## Usage
```
/audit-dependencies [--module <ModuleName>] [--fix]
```

## Description
Analyse l'ensemble des dépendances inter-modules et NuGet du monorepo.
Détecte les violations du graphe de dépendances, les versions incohérentes,
les références circulaires et les packages obsolètes.

## Exemples
```
/audit-dependencies                    ← audit global
/audit-dependencies --module Messaging ← audit d'un module spécifique
/audit-dependencies --fix              ← propose des corrections automatiques
```

## Ce qui est vérifié

### Graphe inter-modules
```
□ Aucune dépendance circulaire
□ Toutes les dépendances respectent le graphe autorisé dans CLAUDE.md
□ Les .Abstractions ne dépendent pas d'implémentations
□ Les ProjectReferences en prod pointent uniquement vers .Abstractions
□ Pas de référence directe entre modules en dehors des packages publiés
```

### Cohérence des versions NuGet (Directory.Packages.props)
```
□ Même version d'un package utilisée dans tous les modules
□ Pas de packages dupliqués avec des versions différentes
□ Packages obsolètes ou avec CVE connues (via dotnet list package --vulnerable)
□ Packages MicroKit inter-modules : en version stable si le module est stable
```

### version.json par module
```
□ Chaque module actif a un version.json
□ Format valide Nerdbank.GitVersioning
□ publicReleaseRefSpec configuré
□ Pas de version "0.0" sur un module en production
```

### .csproj par projet
```
□ Pas de version explicite sur les PackageReference (doit être dans Directory.Packages.props)
□ TargetFramework = net9.0 (ou conforme à la politique du module)
□ Nullable = enable
□ ImplicitUsings = enable
□ GenerateDocumentationFile = true pour les projets src/
```

## Output format

```
🔍 Dependency Audit — MicroKit Monorepo

📦 Inter-module dependencies
  ✅ No circular dependencies detected
  ✅ All dependencies respect the allowed graph
  ⚠️  MicroKit.Messaging references MicroKit.Caching.csproj directly
      Expected: PackageReference to MicroKit.Caching NuGet package
      Fix: replace ProjectReference with PackageReference

📋 NuGet versions (Directory.Packages.props)
  ✅ All packages use central version management
  🔴 Microsoft.Extensions.DependencyInjection: 3 different versions across modules
      Result: 8.0.0 / MediatR: 9.0.0 / Messaging: 8.0.7
      Fix: align to 9.0.0

🏷️  Module versions
  ✅ MicroKit.Result: version.json valid (1.2)
  ✅ MicroKit.MediatR: version.json valid (1.0)
  ⚠️  MicroKit.Domain: version.json missing
      Fix: create modules/MicroKit.Domain/version.json

📄 Project files
  ✅ All .csproj use central package management
  ⚠️  MicroKit.Logging: GenerateDocumentationFile not set
      Fix: add <GenerateDocumentationFile>true</GenerateDocumentationFile>

Summary: 1 error, 3 warnings
```
