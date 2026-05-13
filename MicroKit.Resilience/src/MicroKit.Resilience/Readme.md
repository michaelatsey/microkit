# MicroKit.Resilience

**MicroKit.Resilience** est une bibliothèque de résilience robuste et modulaire pour .NET 9+, basée sur **Polly**. Elle permet de protéger vos microservices contre les pannes transitoires (SQL, Réseau, Timeouts) en utilisant des stratégies de Retry, Circuit Breaker et Fallback, tout en restant parfaitement intégrée à **MediatR**.

## Caractéristiques

* **Multi-Détecteurs** : Identification intelligente des erreurs "retryable" pour SQL Server et HTTP.
* **Pipeline Natif Polly v9+** : Utilisation des dernières API de Polly pour des performances optimales.
* **Intégration MediatR** : Application automatique des politiques via un `IPipelineBehavior`.
* **Découplage Total** : Votre logique métier (Command/Query) ne dépend pas de Polly.
* **Protection Avancée** : Inclut un Disjoncteur (Circuit Breaker) et un filet de sécurité (Fallback).

---

## Installation

Référencez les projets suivants dans votre solution :

* `MicroKit.Resilience` (Core)
* `MicroKit.Resilience.Data.SqlServer`
* `MicroKit.Resilience.Http`

---

## Configuration

Dans votre `Program.cs`, configurez la résilience en quelques lignes :

```csharp
builder.Services.AddMicroKitResilience()
    .AddSqlServer() // Détection des deadlocks et coupures SQL
    .AddHttp()      // Détection des timeouts et erreurs 5xx
    .AddDefaultRetryPolicy(options => 
    {
        options.PipelineName = "DefaultRetry";
        options.RetryCount = 3;
        options.EnableCircuitBreaker = true; // Protection contre l'effet domino
        options.EnableFallback = true;       // Exception métier finale propre
    });

```

---

## Utilisation avec MediatR

### 1. Mode Automatique

Par défaut, toutes vos requêtes MediatR passeront par le `ResilienceBehavior` et utiliseront la politique `"DefaultRetry"`.

### 2. Mode Personnalisé

Si une commande nécessite une stratégie spécifique (ex: plus agressive ou plus lente), implémentez l'interface `IResilientRequest` :

```csharp
public record SyncExternalDataCommand(int Id) : ICommand, IResilientRequest
{
    // Cette commande utilisera un pipeline nommé différemment
    public string PipelineName => "HeavyStrategy";
}

```

---

## Architecture du Pipeline

L'ordre d'exécution est crucial pour garantir une protection maximale :

1. **Fallback** (Extérieur) : Capture l'échec final si rien n'a fonctionné et lève une exception claire.
2. **Circuit Breaker** : Interrompt immédiatement les appels si le taux d'échec dépasse 50%.
3. **Retry** (Intérieur) : Tente de résoudre les erreurs passagères avec un délai exponentiel et du *Jitter*.

---

## Extension du Framework

Vous pouvez créer vos propres détecteurs d'erreurs en implémentant `IResilienceStrategyDetector` :

```csharp
public class MyCustomDetector : IResilienceStrategyDetector
{
    public bool CanHandle(Exception ex) => ex is MyCustomException;
    public bool ShouldRetry(Exception ex) => true;
}

// Enregistrement
builder.Services.AddMicroKitResilience().AddDetector<MyCustomDetector>();

```

---

## Bonnes Pratiques

* **Idempotence** : Assurez-vous que vos Handlers sont idempotents, car ils peuvent être exécutés plusieurs fois en cas de retry.
* **Monitoring** : Surveillez les logs pour identifier si le Circuit Breaker s'ouvre fréquemment, ce qui indique un problème structurel chez un fournisseur.
* **Timeouts** : Utilisez toujours un `CancellationToken` dans vos requêtes pour permettre à Polly de couper les appels trop longs.
