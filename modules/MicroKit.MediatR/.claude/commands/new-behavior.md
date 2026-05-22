# Command: /new-behavior

## Usage
```
/new-behavior <BehaviorName> [--order <number>] [--marker <IMarkerName>] [--scope <all|commands|queries>] [--short-circuit]
```

## Description
Génère un pipeline behavior personnalisé correctement ordonné, avec son marker opt-in,
son enregistrement DI et son test de base.

## Exemples
```
/new-behavior Audit --order 150 --marker IAuditableRequest --scope all
/new-behavior RateLimit --order 250 --marker IRateLimitedRequest --scope commands --short-circuit
/new-behavior Encryption --order 450 --marker IEncryptedQuery --scope queries
```

## Ce qui est généré

### Marker interface
```csharp
/// <summary>
/// Marker interface that opts a request into the <see cref="{Name}Behavior{TRequest,TResponse}"/>.
/// </summary>
/// <example>
/// <code>
/// public sealed record MySensitiveCommand(...) : ICommand&lt;Result&lt;Unit&gt;&gt;, I{Marker}
/// {
///     // properties required by I{Marker}
/// }
/// </code>
/// </example>
public interface I{Marker}
{
    // TODO: ajouter les propriétés de configuration si nécessaire
}
```

### Behavior
```csharp
/// <summary>
/// Pipeline behavior that [description].
/// Activated for requests implementing <see cref="I{Marker}"/>.
/// Pipeline order: <see cref="PipelineOrder.{Name}"/> ({order}).
/// </summary>
public sealed class {Name}Behavior<TRequest, TResponse>(
    ILogger<{Name}Behavior<TRequest, TResponse>> logger
    /* + autres dépendances */)
    : BehaviorBase<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <inheritdoc/>
    public override int Order => PipelineOrder.{Name}; // = {order}

    /// <inheritdoc/>
    public override async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Guard : opt-in uniquement
        if (request is not I{Marker} {markerVar})
            return await next().ConfigureAwait(false);

        // TODO: logique pre-handler
        logger.LogDebug("[{Name}] Processing {Request}", typeof(TRequest).Name);

        // Si --short-circuit : vérifier la condition de court-circuit ici
        // if (shouldShortCircuit) return CreateShortCircuitResponse(...);

        var response = await next().ConfigureAwait(false);

        // TODO: logique post-handler (si nécessaire)

        return response;
    }
}
```

### PipelineOrder entry (à ajouter manuellement dans PipelineOrder.cs)
```csharp
// Dans PipelineOrder.cs, ajouter :
public const int {Name} = {order};
// Entre {order-1} et {order+1} — vérifier la cohérence
```

### Enregistrement DI
```csharp
// Dans ServiceCollectionExtensions.cs :
services.AddTransient(typeof(IPipelineBehavior<,>), typeof({Name}Behavior<,>));
// Ou en fluent si tu utilises AddMicroKitMediatR :
cfg.Add{Name}Behavior();
```

### Test
```csharp
public sealed class {Name}BehaviorTests
{
    [Fact]
    public async Task Handle_WhenMarkerPresent_Applies{Name}Logic()
    {
        // TODO: setup behavior + next delegate
        // Assert: comportement attendu
    }

    [Fact]
    public async Task Handle_WhenMarkerAbsent_PassesThrough()
    {
        // Assert: next() appelé une fois, sans modification
    }

    // Si --short-circuit :
    [Fact]
    public async Task Handle_WhenConditionMet_ShortCircuitsWithoutCallingNext()
    {
        // Assert: next() NOT called
    }
}
```

## Règles appliquées
1. `sealed class` + primary constructor
2. Héritage de `BehaviorBase<TRequest, TResponse>`
3. `Order` défini explicitement via `PipelineOrder`
4. Guard pattern en première ligne (opt-in via marker)
5. `ConfigureAwait(false)` sur tous les awaits
6. Pas d'appel à `IMediator` depuis le behavior
7. XML docs générés
