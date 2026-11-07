# Command: /impl-result

## Usage
```
/impl-result <ServiceName> --operation <description> [--input <Type>] [--output <Type>] [--api]
```

## Description
Génère le squelette complet d'un service/handler asynchrone utilisant Result, avec les erreurs associées,
le test correspondant et le mapping HTTP si applicable.

## Exemple
```
/impl-result UserService --operation "get user by id" --input "Guid userId" --output UserDto --api
/impl-result OrderHandler --operation "create order from cart" --input CreateOrderCommand --output OrderId
```

## Ce qui est généré

### 1. Interface + Implémentation
```csharp
public interface I{ServiceName}
{
    ValueTask<Result<{Output}>> {MethodName}Async({Input}, CancellationToken ct = default);
}

public sealed class {ServiceName}(IDependency dep) : I{ServiceName}
{
    public async ValueTask<Result<{Output}>> {MethodName}Async({Input}, CancellationToken ct = default)
    {
        // Pattern :
        // 1. Valider l'entrée : un échec prévisible retourne Result.Failure
        // 2. Récupérer ou calculer, en passant ct ; l'annulation de l'appelant se propage
        // 3. Mapper vers la sortie
        {Entity}? entity = await dep.FindAsync(id, ct).ConfigureAwait(false);
        return entity is null
            ? Result.Failure<{Output}>(new {Entity}NotFoundError(id))
            : Result.Success(entity.To{Output}());
    }
}
```

Une dépendance d'infrastructure qui lève se traite à la frontière (`rules/no-exceptions-flow.md`) : l'exception part dans le log par `[LoggerMessage]`, l'erreur porte un message générique.

### 2. Erreurs métier associées
Génère automatiquement les erreurs probables selon le contexte:
- `{Entity}NotFoundError` si lecture
- `{Entity}AlreadyExistsError` si création
- `{Entity}ConflictError` si update
- `{Operation}ForbiddenError` (catégorie `Forbidden`) si opération sensible refusée à un utilisateur identifié

### 3. Test skeleton
```csharp
public sealed class {ServiceName}Tests
{
    private readonly I{Dependency} _mockDep = Substitute.For<I{Dependency}>();
    private readonly {ServiceName} _sut;
    
    public {ServiceName}Tests() => _sut = new {ServiceName}(_mockDep);

    [Fact]
    public async Task {MethodName}Async_WhenNotFound_ReturnsFailure()
    {
        // Arrange
        _mockDep.FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(({Entity}?)null);

        // Act
        Result<{Output}> result = await _sut.{MethodName}Async(Guid.NewGuid());

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<{Entity}NotFoundError>();
    }

    [Fact]
    public async Task {MethodName}Async_WhenCallerCancels_PropagatesCancellation()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _mockDep.FindAsync(Arg.Any<Guid>(), cts.Token)
            .Returns<Task<{Entity}?>>(_ => Task.FromCanceled<{Entity}?>(cts.Token));

        // Act + Assert
        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await _sut.{MethodName}Async(Guid.NewGuid(), cts.Token));
    }
}
```

Mêmes structures pour les autres cas : `{MethodName}Async_WhenValid_ReturnsSuccess`, `{MethodName}Async_WhenForbidden_ReturnsFailure`.

### 4. Endpoint minimal API (si --api flag)
```csharp
app.MapGet("/api/{entity}/{id}", async (
    Guid id,
    I{ServiceName} service,
    CancellationToken ct) =>
    (await service.{Method}Async(id, ct)).ToHttpResult());
```

## Conventions appliquées
- `async` → `ValueTask<Result<T>>` (convention racine, mesurée face à `Task<T>` dans `README.md`, Performance Notes). Les délégués async des extensions (`BindAsync`, `MapAsync`…) retournent `Task` : un port généré s'y compose par un lambda async, `BindAsync(async x => await svc.{MethodName}Async(x, ct))` (écart, `.claude/CLAUDE.md`)
- `CancellationToken` toujours en dernier paramètre avec default, transmis aux appels ; l'annulation de l'appelant se propage
- Guard null en entrée via `ArgumentNullException.ThrowIfNull`
- Pas d'exception dans l'implémentation métier
