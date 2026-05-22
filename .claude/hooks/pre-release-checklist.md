# Hook: Pre-Release Checklist

## Déclenchement
Avant chaque commande /release ou création de tag Git.

## Vérifications bloquantes (MUST pass)

### CI
- [ ] Dernier run CI sur `main` : vert pour le module ciblé
- [ ] Aucune PR ouverte avec label `breaking-change` non documentée
- [ ] Aucun issue GitHub ouvert avec label `release-blocker`

### Code
- [ ] Aucun `throw new NotImplementedException()` dans le code src/ du module
- [ ] Aucun `// TODO` sans référence à un ticket dans l'API publique
- [ ] Aucun `[Obsolete]` sans message de migration

### Versioning
- [ ] `version.json` du module reflète la version cible
- [ ] Si MAJOR bump : breaking changes documentés dans CHANGELOG.md
- [ ] Si dépendances MicroKit inter-modules : toutes en version stable publiée

### Documentation
- [ ] `CHANGELOG.md` du module contient la section `## [x.y.z]` avec date
- [ ] `README.md` du module à jour (exemples, badges de version)
- [ ] XML docs générées sans erreur (`dotnet build -warnaserror`)

### NuGet
- [ ] `PackageId`, `Description`, `Authors`, `RepositoryUrl` dans le .csproj
- [ ] `PackageLicenseExpression` défini
- [ ] `PackageReadmeFile` pointant vers README.md

## Vérifications recommandées (SHOULD pass)

- [ ] Benchmarks exécutés — pas de régression de performance > 10%
- [ ] Architecture tests verts
- [ ] Coverage >= seuil du module (défini dans settings.json du module)

## Format de sortie

```
🔍 Pre-Release Checklist — MicroKit.Result v1.2.0

✅ CI: last run green (2026-05-22 14:32)
✅ No NotImplementedException in src/
✅ version.json: "1.2" ✓
✅ CHANGELOG.md: section [1.2.0] found
⚠️  README.md: version badge still shows 1.1.0 → update before releasing
❌ XML docs: 2 warnings in ResultExtensions.cs (lines 142, 187) → fix required

Status: BLOCKED (1 error, 1 warning)
Run /release Result --version 1.2.0 again after fixes.
```
