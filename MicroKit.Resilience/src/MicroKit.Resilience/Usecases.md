## 🎯 Cas d'Usage Détaillés

Le framework permet d'adapter la "force" de la protection en fonction de la fragilité de la ressource appelée.

### 1. La Protection Standard (Le "Bouclier" par défaut)

**Scénario** : Une mise à jour de profil utilisateur en base de données.

* **Risque** : Très faible. Une micro-coupure réseau.
* **Comportement** : Pas d'interface `IResilientRequest`. Le système applique la politique globale (ex: 2 tentatives rapides).

```csharp
// Aucun effort requis, la protection est automatique via le Behavior
public record UpdateProfileCommand(string Bio) : ICommand;

```

### 2. La Protection SQL (Le "Spécialiste")

**Scénario** : Une commande complexe avec plusieurs écritures.

* **Risque** : **Deadlocks SQL** fréquents sous haute charge.
* **Comportement** : Utilise le détecteur SQL spécifique. Réessaie 3 fois avec un backoff exponentiel court (1s, 2s, 4s).

```csharp
public record CreateOrderCommand(Guid OrderId) : ICommand, IResilientRequest
{
    // Cible le pipeline configuré avec .AddSqlServer()
    public string PipelineName => "SqlRetry"; 
}

```

### 3. La Protection "Service Tiers" (La "Patience")

**Scénario** : Appel à une API externe de livraison ou de paiement.

* **Risque** : **Saturation du service distant** ou lenteur réseau.
* **Comportement** : On ne veut pas bombarder le partenaire. On configure un retry très lent (ex: toutes les 5 secondes) et un **Circuit Breaker** agressif pour arrêter d'appeler si le partenaire tombe.

```csharp
public record SendNotificationCommand(string Message) : ICommand, IResilientRequest
{
    // Cible une politique lente avec un disjoncteur sensible
    public string PipelineName => "ExternalServiceRetry";
}

```

---

## ⚙️ Configuration des Stratégies

Voici comment ces cas d'usage se traduisent techniquement dans votre couche Infrastructure (`Program.cs`) :

```csharp
builder.Services.AddMicroKitResilience()
    .AddSqlServer() 
    .AddHttp()
    // 1. Politique par défaut (utilisée par UpdateProfile)
    .AddDefaultRetryPolicy(opt => {
        opt.PipelineName = "DefaultRetry";
        opt.RetryCount = 2;
    })
    // 2. Politique SQL dédiée (utilisée par CreateOrder)
    .AddDefaultRetryPolicy(opt => {
        opt.PipelineName = "SqlRetry";
        opt.RetryCount = 3;
        opt.BaseDelaySeconds = 1.0;
    })
    // 3. Politique pour services instables (utilisée par SendNotification)
    .AddDefaultRetryPolicy(opt => {
        opt.PipelineName = "ExternalServiceRetry";
        opt.RetryCount = 5;
        opt.BaseDelaySeconds = 5.0; // On laisse souffler le partenaire
        opt.EnableCircuitBreaker = true; 
        opt.FailureRatio = 0.3; // On coupe dès 30% d'échecs
    });

```

---

## 📊 Résumé visuel des flux

| Commande | Interface | Pipeline utilisé | Stratégie |
| --- | --- | --- | --- |
| `UpdateProfile` | Non | `DefaultRetry` | 2 retries, circuit breaker standard. |
| `CreateOrder` | Oui | `SqlRetry` | Détection Deadlock, 3 retries rapides. |
| `SendNotification` | Oui | `ExternalServiceRetry` | 5 retries lents, disjoncteur ultra-sensible. |

