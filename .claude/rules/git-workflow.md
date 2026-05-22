# Rule: Git Workflow — MicroKit

## Toujours actif pour toute opération Git dans le monorepo.

## Branches protégées
```
main    ← jamais de push direct — uniquement via PR approuvée
dev     ← jamais de push direct — uniquement via PR ou merge de feature
```

## Workflow standard (Feature)

```
1. Créer la branche depuis dev
   git checkout dev && git pull
   git checkout -b feature/result/ensure-async

2. Développer avec commits atomiques
   git commit -m "feat(result): add EnsureAsync overload"
   git commit -m "test(result): add EnsureAsync tests"
   git commit -m "docs(result): document EnsureAsync in CHANGELOG"

3. PR vers dev
   - Title = premier commit message
   - Description = template PR
   - Labels appropriés

4. Squash merge ou rebase merge (pas de merge commit)
   → historique linéaire sur dev

5. Release : PR de dev → main + tag
```

## Workflow de release

```
1. Préparer la branche de release depuis dev
   git checkout -b release/result/1.2.0

2. Finaliser CHANGELOG.md, version.json
   git commit -m "chore(result): prepare release 1.2.0"

3. PR vers main
4. Merge (fast-forward uniquement)
5. Tag sur main
   git tag result-v1.2.0 -m "MicroKit.Result 1.2.0"
   git push origin result-v1.2.0
6. Back-merge main → dev
```

## Commits

### Format obligatoire
```
<type>(<scope>): <description courte en impératif>

[body optionnel — pourquoi, pas quoi]

[footers : BREAKING CHANGE, Closes #XX, Co-authored-by]
```

### Types valides
```
feat      → nouvelle fonctionnalité (MINOR bump)
fix       → correction de bug (PATCH bump)
perf      → amélioration de performance (PATCH bump)
refactor  → refactoring sans changement de comportement
test      → ajout/modification de tests uniquement
docs      → documentation uniquement
chore     → maintenance (deps update, config)
build     → système de build
ci        → GitHub Actions
```

### Scopes valides
```
result, mediatr, domain, messaging, persistence, caching,
http, auth, observability, logging, multitenancy, monorepo
```

## Tags Git

```
Convention : {module-kebab}-v{semver}
  result-v1.2.0
  mediatr-v1.0.0
  domain-v0.1.0-beta.1

Jamais :
  v1.2.0          (ambigu — quel module ?)
  MicroKit-1.2.0  (pas de tiret avant la version)
  result-1.2.0    (manque le 'v')
```

## .gitignore global (à la racine)

```
# Build outputs
**/bin/
**/obj/
**/*.user

# NuGet
**/*.nupkg
**/nupkg/

# Secrets
**/*.env
**/*.pfx
**/*.key

# OS
.DS_Store
Thumbs.db

# IDE
.vs/
.idea/
*.suo
*.sln.docstates

# Coverage
**/coverage/
**/*.opencover.xml
**/*.cobertura.xml

# BenchmarkDotNet
**/BenchmarkDotNet.Artifacts/
```
