# Command: /impl-result

## Usage
```
/impl-result <ServiceName> --operation <description> [--input <Type>] [--output <Type>] [--async]
```

## Description
Génère le squelette complet d'un service/handler utilisant Result, avec les erreurs associées,
le test correspondant et le mapping HTTP si applicable.

## Exemple
```
/impl-result UserService --operation "get user by id" --input "Guid userId" --output UserDto --async
/impl-result OrderHandler --operation "create order from cart" --input CreateOrderCommand --output OrderId --async
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
        // TODO: implement
        // Pattern:
        // 1. Validate input
        // 2. Fetch/compute
        // 3. Map to output
        // 4. Return Result
        
        throw new NotImplementedException();
    }
}
```

### 2. Erreurs métier associées
Génère automatiquement les erreurs probables selon le contexte:
- `{Entity}NotFoundError` si lecture
- `{Entity}AlreadyExistsError` si création
- `{Entity}ConflictError` si update
- `Unauthorized{Operation}Error` si opération sensible

### 3. Test skeleton
```csharp
public sealed class {ServiceName}Tests
{
    private readonly I{Dependency} _mockDep = Substitute.For<I{Dependency}>();
    private readonly {ServiceName} _sut;
    
    public {ServiceName}Tests() => _sut = new {ServiceName}(_mockDep);
    
    public sealed class {MethodName}AsyncShould
    {
        [Fact] public async Task ReturnSuccess_WhenValid() { }
        [Fact] public async Task ReturnFailure_WhenNotFound() { }
        [Fact] public async Task ReturnFailure_WhenUnauthorized() { }
    }
}
```

### 4. Endpoint minimal API (si --api flag)
```csharp
app.MapGet("/api/{entity}/{id}", async (
    Guid id,
    I{ServiceName} service,
    CancellationToken ct) =>
    (await service.{Method}Async(id, ct)).ToHttpResult());
```

## Conventions appliquées
- `async` → `ValueTask<Result<T>>` par défaut
- `CancellationToken` toujours en dernier paramètre avec default
- Guard null en entrée via `ArgumentNullException.ThrowIfNull`
- Pas d'exception dans l'implémentation métier
