# Hook: Register Module

## Déclenchement
Automatiquement après /new-module ou /bootstrap-module-claude.

## Objectif
S'assurer que tout nouveau module est correctement enregistré dans tous
les fichiers de configuration du monorepo.

## Actions à effectuer

### 1. Mettre à jour .claude/settings.json
```json
// Ajouter dans moduleRegistry :
"MicroKit.[NewModule]": {
  "path": "modules/MicroKit.[NewModule]",
  "claude": "modules/MicroKit.[NewModule]/.claude/CLAUDE.md",
  "status": "planned",
  "hasDependencies": ["MicroKit.X", "MicroKit.Y"]
}
```

### 2. Mettre à jour .claude/CLAUDE.md
```markdown
// Ajouter dans la table des modules :
| **MicroKit.[NewModule]** | `modules/MicroKit.[NewModule]/` | `modules/MicroKit.[NewModule]/.claude/` | 📋 Planifié |

// Mettre à jour le graphe de dépendances si nécessaire
```

### 3. Ajouter dans MicroKit.slnx
```xml
<Project Path="modules/MicroKit.[NewModule]/MicroKit.[NewModule].slnx" />
```

### 4. Créer le workflow CI
```
Copier .github/workflows/ci-result.yml
Renommer en ci-[module-kebab].yml
Adapter :
  - name du workflow
  - paths de changeset detection (modules/MicroKit.[NewModule]/**)
  - nom du projet de test
```

### 5. Mettre à jour build/Directory.Packages.props
```
Ajouter les packages NuGet tiers requis par ce module
si non déjà présents dans le fichier central
```

## Validation post-enregistrement
```
✅ Module registré dans .claude/settings.json
✅ .claude/CLAUDE.md tableau mis à jour
✅ Solution racine mise à jour
✅ Workflow CI créé : .github/workflows/ci-[module-kebab].yml
✅ Packages ajoutés dans Directory.Packages.props (si nécessaire)
```
