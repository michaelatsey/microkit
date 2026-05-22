# Hook: Detect Module Context

## Déclenchement
Automatiquement avant chaque tâche dans le monorepo.

## Objectif
Identifier le module concerné par la tâche et charger son `.claude/CLAUDE.md`
avant toute action. Évite de travailler avec uniquement le contexte global
quand une tâche est spécifique à un module.

## Algorithme de détection

```
1. Le chemin du fichier concerné contient "modules/MicroKit.[X]/" ?
   → Module = MicroKit.[X]
   → Charger modules/MicroKit.[X]/.claude/CLAUDE.md

2. La tâche mentionne explicitement un module ("dans Result", "pour MediatR") ?
   → Module = module mentionné
   → Charger modules/MicroKit.[X]/.claude/CLAUDE.md

3. La tâche touche à build/, .github/, eng/ ?
   → Contexte = monorepo global
   → Rester sur .claude/CLAUDE.md + .claude/rules/

4. La tâche touche plusieurs modules ?
   → Charger ce fichier + chaque .claude/CLAUDE.md des modules concernés
   → Consulter l'agent monorepo-orchestrator si les modules ont des dépendances

5. Contexte ambigu ?
   → Demander à l'utilisateur : "Quel module est concerné ?"
```

## Output attendu (silencieux)
Le contexte est chargé sans narration.
Si le `.claude/CLAUDE.md` du module est absent → signaler et proposer `/bootstrap-module-claude`.

## Cas particulier : module non encore bootstrappé
```
⚠️  Aucun .claude/ trouvé pour MicroKit.[X]
    Ce module n'a pas encore de cerveau configuré.
    Utilise /bootstrap-module-claude [X] pour le générer avant de commencer.
```
