# Skill: Release Process — MicroKit

## Quand activer ce skill
- Préparation d'une release de module
- Résolution d'un problème de versioning Nerdbank
- Configuration d'un nouveau workflow de release
- Coordination d'une release multi-modules

## Nerdbank.GitVersioning — fonctionnement

### version.json par module
```json
{
  "version": "1.0",
  "publicReleaseRefSpec": [
    "^refs/heads/main$",
    "^refs/tags/result-v\\d+\\.\\d+\\.\\d+$"
  ],
  "pathFilters": [
    "modules/MicroKit.Result/"
  ],
  "cloudBuild": {
    "setVersionVariables": true,
    "buildNumber": {
      "enabled": true
    }
  },
  "release": {
    "tagName": "result-v{version}",
    "branchName": "release/result/{version}"
  }
}
```

### Comment Nerdbank calcule la version
```
Sur main après tag result-v1.2.0       → 1.2.0 (public release)
Sur main sans tag                       → 1.2.0+build.42 (non-public)
Sur dev                                 → 1.2.0-dev.{height}
Sur feature/result/xxx                  → 1.2.0-feature-result-xxx.{height}
Sur release/result/1.3.0               → 1.3.0-beta.{height}
```

## GitHub Actions — release.yml

```yaml
name: Release

on:
  push:
    tags:
      - '*-v*'   # Déclenché par tout tag de la forme [module]-v[version]

jobs:
  release:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0   # Nécessaire pour Nerdbank.GitVersioning

      - name: Detect module from tag
        id: module
        run: |
          TAG="${{ github.ref_name }}"
          MODULE=$(echo $TAG | sed 's/-v.*//')
          echo "name=$MODULE" >> $GITHUB_OUTPUT
          echo "path=modules/MicroKit.$(echo $MODULE | sed 's/-//' | awk '{print toupper(substr($0,1,1)) substr($0,2)}')" >> $GITHUB_OUTPUT

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json

      - name: Build
        run: dotnet build ${{ steps.module.outputs.path }} -c Release

      - name: Test
        run: dotnet test ${{ steps.module.outputs.path }} -c Release --no-build

      - name: Pack
        run: dotnet pack ${{ steps.module.outputs.path }} -c Release --no-build -o ./nupkg

      - name: Push to NuGet
        run: dotnet nuget push ./nupkg/*.nupkg -k ${{ secrets.NUGET_API_KEY }} -s https://api.nuget.org/v3/index.json

      - name: Create GitHub Release
        uses: softprops/action-gh-release@v2
        with:
          generate_release_notes: true
          files: ./nupkg/*.nupkg
```

## Processus de release manuelle (step by step)

### Single module
```bash
# 1. S'assurer d'être sur main à jour
git checkout main && git pull

# 2. Vérifier que les tests passent
dotnet test modules/MicroKit.Result/MicroKit.Result.slnx -c Release

# 3. Mettre à jour CHANGELOG.md
# (manuellement ou via /release --dry-run pour le générer)

# 4. Commit final
git commit -m "chore(result): prepare release 1.2.0"
git push

# 5. Attendre que CI soit vert

# 6. Créer et pousser le tag
git tag result-v1.2.0 -m "MicroKit.Result 1.2.0"
git push origin result-v1.2.0

# 7. Surveiller GitHub Actions
# https://github.com/[org]/MicroKit/actions
```

### Multi-modules coordonnée
```bash
# Toujours dans l'ordre du graphe de dépendances
# Attendre que chaque release soit publiée sur NuGet avant de releaser le suivant
# (le module dépendant doit pouvoir résoudre le package)

git tag domain-v1.0.0 && git push origin domain-v1.0.0
# attendre publication NuGet (~5 min)

git tag result-v1.2.0 && git push origin result-v1.2.0
# attendre publication NuGet

git tag mediatr-v1.1.0 && git push origin mediatr-v1.1.0
```

## Versioning des dépendances inter-modules dans Directory.Packages.props

```xml
<!-- En développement : pointer vers la dernière stable -->
<PackageVersion Include="MicroKit.Result" Version="1.2.0" />

<!-- En développement actif avec pre-release locale -->
<PackageVersion Include="MicroKit.Result" Version="1.3.0-dev.*" />
<!-- Nécessite un feed NuGet local ou GitHub Packages -->
```
