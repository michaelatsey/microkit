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
# Target: 85% sur MicroKit.Result.Core
# Target: 75% sur MicroKit.Result.Extensions
```

### Analyse statique
```bash
dotnet analyzer run
# Roslyn analyzers: Microsoft.CodeAnalysis.NetAnalyzers
```

## Checklist manuelle avant PR

- [ ] XML docs sur toutes les nouvelles méthodes `public`
- [ ] Tests pour chaque nouveau chemin (success ET failure)
- [ ] `CHANGELOG.md` mis à jour si breaking change
- [ ] Version bumped dans `.csproj` si feature ajoutée
- [ ] `README.md` mis à jour si nouvelle API publique majeure

## Messages d'erreur attendus

```
✅ Build: 0 errors, 0 warnings
✅ Tests: 247 passed, 0 failed, 0 skipped
✅ Format: no changes required
❌ Coverage: 82% (minimum: 85%) — BLOCKED
```
