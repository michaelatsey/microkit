# Skill: New Module Bootstrap

## Quand activer ce skill
- Création d'un nouveau module MicroKit
- Vérification de la complétude d'un module existant
- Migration d'un module vers la structure standard

## Checklist complète de bootstrap (dans l'ordre)

### Phase 1 — Conception (.claude/ AVANT tout)
```
□ 1. Définir la vision du module (1 paragraphe)
□ 2. Identifier les dépendances inter-modules (graphe)
□ 3. Identifier les packages tiers requis
□ 4. Identifier les providers optionnels
□ 5. Générer le .claude/ complet via /bootstrap-module-claude
□ 6. Valider le .claude/ avec l'orchestrateur avant de continuer
```

### Phase 2 — Structure (après validation .claude/)
```
□ 7. Créer la structure de répertoires via /new-module
□ 8. Créer version.json (Nerdbank config)
□ 9. Créer MicroKit.[Module].slnx
□ 10. Ajouter dans MicroKit.slnx (solution racine)
□ 11. Créer les .csproj (Abstractions, Core, Providers, Tests)
□ 12. Configurer Directory.Packages.props (nouveaux packages si besoin)
```

### Phase 3 — CI/CD
```
□ 13. Créer .github/workflows/ci-[module-kebab].yml
□ 14. Configurer changeset detection (paths: modules/MicroKit.[Module]/**)
□ 15. Ajouter le module dans la matrice de coverage (si applicable)
□ 16. Tester le workflow manuellement (workflow_dispatch)
```

### Phase 4 — Documentation initiale
```
□ 17. README.md du module (description, installation, quickstart)
□ 18. docs/architecture/overview.md (décisions de conception)
□ 19. CHANGELOG.md vide avec structure initiale
□ 20. Mise à jour du README.md racine du monorepo
```

### Phase 5 — Enregistrement global
```
□ 21. .claude/settings.json moduleRegistry mis à jour
□ 22. .claude/CLAUDE.md tableau des modules mis à jour
□ 23. Graphe de dépendances mis à jour si nouvelles liaisons
```

## Template version.json
```json
{
  "version": "1.0",
  "publicReleaseRefSpec": [
    "^refs/heads/main$",
    "^refs/tags/[module-kebab]-v\\d+\\.\\d+\\.\\d+(-.+)?$"
  ],
  "pathFilters": [
    "modules/MicroKit.[Module]/"
  ],
  "cloudBuild": {
    "setVersionVariables": true
  },
  "release": {
    "tagName": "[module-kebab]-v{version}",
    "branchName": "release/[module-kebab]/{version}"
  }
}
```

## Template .csproj Abstractions
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>MicroKit.[Module].Abstractions</AssemblyName>
    <RootNamespace>MicroKit.[Module]</RootNamespace>
    <Description>Abstractions and contracts for MicroKit.[Module]</Description>
    <PackageTags>microkit;[module-lower];abstractions;dotnet</PackageTags>
  </PropertyGroup>

  <!-- Dépendances sur .Abstractions d'autres modules uniquement -->
  <ItemGroup>
    <PackageReference Include="MicroKit.Result.Abstractions" />
  </ItemGroup>
</Project>
```

## Template .csproj Core
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>MicroKit.[Module]</AssemblyName>
    <RootNamespace>MicroKit.[Module]</RootNamespace>
    <Description>[Description courte du module]</Description>
    <PackageTags>microkit;[module-lower];dotnet</PackageTags>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../MicroKit.[Module].Abstractions/MicroKit.[Module].Abstractions.csproj" />
  </ItemGroup>

  <ItemGroup>
    <!-- Packages tiers — versions dans Directory.Packages.props -->
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
  </ItemGroup>
</Project>
```

## Template ci-[module].yml
```yaml
name: CI — MicroKit.[Module]

on:
  push:
    branches: [main, dev]
    paths:
      - 'modules/MicroKit.[Module]/**'
      - 'build/**'
      - '.github/workflows/ci-[module-kebab].yml'
  pull_request:
    paths:
      - 'modules/MicroKit.[Module]/**'
      - 'build/**'

jobs:
  build-and-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0

      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json

      - name: Build
        run: dotnet build modules/MicroKit.[Module]/MicroKit.[Module].slnx -c Release

      - name: Unit Tests
        run: dotnet test modules/MicroKit.[Module]/tests/MicroKit.[Module].UnitTests -c Release --no-build

      - name: Integration Tests
        run: dotnet test modules/MicroKit.[Module]/tests/MicroKit.[Module].IntegrationTests -c Release --no-build

      - name: Architecture Tests
        run: dotnet test modules/MicroKit.[Module]/tests/MicroKit.[Module].ArchitectureTests -c Release --no-build

      - name: Upload Coverage
        uses: codecov/codecov-action@v4
        with:
          flags: [module-kebab]
```
