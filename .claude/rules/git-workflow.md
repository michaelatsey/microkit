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

## ⚠ Corrections post-review — le piège du squash-merge

**Ne jamais pousser sur la branche source d'une PR déjà mergée.** GitHub ne rouvre pas une PR
fermée quand sa branche reçoit un nouveau commit, et n'émet aucun signal : ni notification, ni
statut, ni conflit. Le commit reste sur une branche fermée et personne ne le voit.

C'est arrivé sur `MicroKit.Messaging` #93 : les corrections de l'`api-reviewer` ont été poussées
**deux minutes après** le squash-merge. Elles sont restées orphelines pendant que #94, #95 et #96
se construisaient sur la colonne non renommée — quatre PR de dérive, détectées seulement en
relisant un nom de colonne.

```
✅ Correction après merge  → nouvelle branche + nouvelle PR (fix/<scope>/<desc>)
❌ Correction après merge  → push sur la branche de la PR mergée
```

### Détection

Une branche squash-mergée n'a aucun commit en commun avec `dev` : `git log dev..<branche>` est donc
toujours non vide et ne prouve rien. Ce qui prouve qu'une branche a bien atterri, c'est que **l'arbre
de son tip existe quelque part dans l'historique de `dev`** :

```bash
git rev-list dev --format="%T" | grep -v '^commit' | sort -u > /tmp/devtrees
git rev-parse <branche>^{tree} | grep -qf /tmp/devtrees || echo "TIP TREE NOT ON DEV"
```

À passer sur toutes les branches distantes après une série de PR, ou avant d'ouvrir un lot qui
dépend du précédent.

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
http, auth, observability, logging, tenancy, monorepo
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
