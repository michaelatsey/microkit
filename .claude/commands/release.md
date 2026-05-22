# Command: /release

## Usage
```
/release <ModuleName> [--version <semver>] [--pre <alpha|beta|rc>] [--dry-run]
/release all [--dry-run]
```

## Description
Orchestre la release d'un ou plusieurs modules MicroKit.
Vérifie les prérequis, génère le changelog, crée le tag Git dans le bon ordre.

## Exemples
```
/release Result --version 1.2.0
/release MediatR --pre rc.1
/release all --dry-run          ← simulation sans commit ni tag
/release Result,MediatR         ← release coordonnée (ordonnée par dépendances)
```

## Process exécuté

### 1. Vérifications pré-release
```
Pour chaque module ciblé :
□ CI vert sur main (vérifier le dernier run GitHub Actions)
□ Pas de commits non mergés sur des branches feature/* actives
□ version.json à jour
□ CHANGELOG.md contient une section pour la version cible
□ Dépendances MicroKit inter-modules : toutes en version stable
□ Aucun package NuGet en pre-release dans Directory.Packages.props
  (sauf si la release courante est elle-même une pre-release)
```

### 2. Ordre de release automatique
```
Si plusieurs modules ciblés, ordonne automatiquement par graphe de dépendances.
Affiche l'ordre prévu et demande confirmation avant de continuer.

Exemple pour /release Result,MediatR :
  Ordre détecté : Result (1) → MediatR (2)
  Raison : MediatR dépend de Result
```

### 3. Génération / validation du changelog
```
Analyse les commits depuis le dernier tag du module
Format : Conventional Commits → Keep a Changelog
Affiche le changelog généré pour validation manuelle
```

### 4. Création du tag (si --dry-run absent)
```bash
git tag [module-kebab]-v[version] -m "MicroKit.[Module] [version]"
git push origin [module-kebab]-v[version]
```

### 5. Output
```
🚀 Release MicroKit.Result 1.2.0

✅ Pre-checks passed
📋 Changelog:
  ### Added
  - EnsureAsync overload (#42)
  ### Fixed
  - Map allocation on failure path (#45)

🏷️  Tag created: result-v1.2.0
📤 Pushed to origin

⏳ GitHub Actions release workflow triggered
   Monitor: https://github.com/[org]/MicroKit/actions
```

## Mode --dry-run
Affiche tout ce qui serait fait sans créer de tag ni pusher.
Utile pour valider le changelog et l'ordre avant de committer.
