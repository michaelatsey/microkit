# MicroKit.Cqrs

`MicroKit.Cqrs` est un écosystème modulaire pour bâtir des applications CQRS robustes avec MediatR et Autofac. Son architecture découpée permet de n'embarquer que le strict nécessaire.

## Structure des Packages

| Package | Description |
| --- | --- |
| **MicroKit.Cqrs.Abstractions** | Contrats de base pour le CQRS (ICommand, IQuery). |
| **MicroKit.Cqrs.MediatR.Abstractions** | Interfaces spécifiques à MediatR (ICacheableRequest, etc.). |
| **MicroKit.Cqrs.MediatR.Behaviors** | Implémentations standard des Behaviors (Logging, Validation, etc.). |
| **MicroKit.Cqrs.MediatR.Caching** | Pipelines MediatR dédiés à la gestion du cache. |
| **MicroKit.Cqrs.MediatR.Autofac** | Moteur d'enregistrement et Fluent Builder pour Autofac. |

## Installation & Setup

Pour utiliser la stack complète avec le caching, assurez-vous de référencer les packages correspondants.

### Configuration du Container (Autofac)

```csharp
builder.Host.ConfigureContainer<ContainerBuilder>(container =>
{
    container.AddMicroKitCqrs(builder =>
    {
        // Configuration globale (Scan des assemblies)
        builder.Configure(options =>
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            options.AddAssemblies([.. assemblies]);
        });

        // Configuration du Pipeline MediatR
        builder.UseMediatRModule(mediatrCfg =>
        {
            // Requis pour utiliser le CachingBehavior
            // Provient de MicroKit.Cqrs.MediatR.Caching
            mediatrCfg.UseDistributedCache();

            // Enregistrement des Behaviors avec gestion de l'ordre
            // Les types proviennent de MicroKit.Cqrs.MediatR.Behaviors
            mediatrCfg.AddPipeline(typeof(SecurityContextBehavior<,>), -200);
            mediatrCfg.AddPipeline(typeof(ValidationBehavior<,>), -150);
            mediatrCfg.AddPipeline(typeof(LoggingBehavior<,>), -100);
            mediatrCfg.AddPipeline(typeof(ResilienceBehavior<,>), 10);
            
            // Comportements de Cache (MicroKit.Cqrs.MediatR.Caching)
            mediatrCfg.AddPipeline(typeof(CachingBehavior<,>), 20); 
            
            mediatrCfg.AddPipeline(typeof(TransactionBehavior<,>), 30);
            mediatrCfg.AddPipeline(typeof(IdempotencyBehavior<,>), 500);
            
            // Invalidation finale
            mediatrCfg.AddPipeline(typeof(CacheInvalidationBehavior<,>), 1000);
        });
    });
});

```

## Détails du Pipeline (Ordre d'exécution)

Le système utilise un tri numérique pour orchestrer les Behaviors :

1. **Sécurité (-200)** : Première barrière à l'entrée.
2. **Validation (-150)** : Vérifie l'intégrité du message.
3. **Logging (-100)** : Enregistre la commande (uniquement si valide/autorisée).
4. **Résilience (10)** : Gère les politiques de retry.
5. **Caching Read (20)** : Court-circuite le Handler si la donnée est en cache.
6. **Transaction (30)** : Démarre le scope de persistance.
7. **Idempotence (500)** : Vérifie les doublons au sein de la transaction.
8. **Cache Invalidation (1000)** : Nettoie les entrées obsolètes en cas de succès.

## Pourquoi ce découpage ?

Cette séparation en plusieurs packages vous permet de :

* Utiliser les **Abstractions** dans vos couches de Domain/Application sans dépendre d'Autofac ou de l'implémentation des Behaviors.
* Remplacer le module **Autofac** par un autre moteur sans impacter vos Behaviors.
* Ne pas inclure la logique de **Caching** si votre microservice n'en a pas l'utilité.
