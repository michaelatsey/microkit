# Rappel clé MediatR (important)
L’ordre d’exécution des IPipelineBehavior est l’ordre d’enregistrement,
mais l’exécution est en “oignon” (nested) :

- Le premier enregistré est le premier à entrer
- Le dernier enregistré est le plus proche du Handler
- Puis on remonte dans l’ordre invers

