# Hook: Pre-Commit Validation Rules

## Déclenchement
Avant chaque commit ou après chaque modification de fichier `.cs`.

## Vérifications obligatoires (bloquantes)

### 1. Compilation propre
```bash
dotnet build --no-restore -warnaserror
# Zéro warning toléré en CI
```

### 2. Tests verts
```bash
dotnet test --no-build --filter "Category!=Integration"
# Tous les tests unitaires doivent passer
```

### 3. Format cohérent
```bash
dotnet format --verify-no-changes
# Aucune divergence de style autorisée
```

### 4. Nullable warnings
Vérifier qu'aucun `CS8600`, `CS8601`, `CS8602`, `CS8603`, `CS8604` n'est présent.

### 5. Pas de `TODO` sans ticket
```
# Autorisé: // TODO(MKRT-42): implement X
# Interdit: // TODO: implement X  (sans référence)
```

## Vérifications conseillées (non-bloquantes)

### Coverage minimum
```bash
dotnet test --collect:"XPlat Code Coverage"
# Target: 85% sur src/MicroKit.Result/ hors Extensions/
# Target: 75% sur src/MicroKit.Result/Extensions/
```

### Analyse statique
```bash
dotnet build -c Release
# Pas de commande séparée : les analyzers du SDK (Microsoft.CodeAnalysis.NetAnalyzers) s'exécutent pendant le build
```

## Checklist manuelle avant PR

- [ ] XML docs sur toutes les nouvelles méthodes `public`
- [ ] Tests pour chaque nouveau chemin (success ET failure)
- [ ] `CHANGELOG.md` mis à jour si breaking change
- [ ] Aucune version dans les `.csproj` : versions et releases relèvent d'ADR-GLOBAL-002
- [ ] `README.md` mis à jour si nouvelle API publique majeure

## Sorties réelles

```
# dotnet build
Build succeeded.
    0 Warning(s)
    0 Error(s)

# dotnet test : seul ArchitectureTests contient des tests
Passed!  - Failed:     0, Passed:    44, Skipped:     0, Total:    44, Duration: 65 ms - MicroKit.Result.ArchitectureTests.dll (net10.0)

# dotnet format --verify-no-changes : aucune sortie et code 0 s'il n'y a rien à changer, sinon code 2 et une ligne par divergence
<fichier>(<ligne>,<colonne>): error WHITESPACE: Fix whitespace formatting. [...]
```
