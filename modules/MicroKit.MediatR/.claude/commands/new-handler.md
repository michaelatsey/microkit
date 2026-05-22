# Command: /new-handler

## Usage
```
/new-handler <HandlerName> --type <command|query|event|stream> [--result] [--input <Fields>] [--output <Type>] [--behaviors <validation,cache,retry,idempotent,auth>]
```

## Description
Génère un handler CQRS complet : contrat (ICommand/IQuery), handler, test skeleton,
et l'enregistrement DI correspondant.

## Exemples
```
/new-handler CreateOrder --type command --result --input "Guid userId, OrderItem[] items" --output OrderId --behaviors validation,idempotent

/new-handler GetUserById --type query --result --input "Guid userId" --output UserDto --behaviors validation,cache

/new-handler UserRegistered --type event --input "Guid userId, string email, DateTimeOffset registeredAt"

/new-handler GetProductsFeed --type stream --input "string category" --output ProductDto
```

## Ce qui est généré

### `--type command --result`
```csharp
// 1. Contrat
/// <summary>[Description de la commande].</summary>
public sealed record CreateOrderCommand(Guid UserId, OrderItem[] Items)
    : ICommand<Result<OrderId>>, IIdempotentCommand  // si --behaviors idempotent
{
    public string IdempotencyKey => $"create-order:{UserId}:{/* hash items */}";
}

// 2. Validator (si --behaviors validation)
public sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Items).NotEmpty().WithMessage("Order must contain at least one item.");
    }
}

// 3. Handler
public sealed class CreateOrderHandler(
    IOrderRepository repo,
    IDomainEventDispatcher events)
    : ICommandHandler<CreateOrderCommand, Result<OrderId>>
{
    public async ValueTask<Result<OrderId>> Handle(
        CreateOrderCommand command,
        CancellationToken ct = default)
    {
        // TODO: implement
        throw new NotImplementedException();
    }
}

// 4. Enregistrement DI (dans ServiceCollectionExtensions ou module)
services.AddTransient<ICommandHandler<CreateOrderCommand, Result<OrderId>>, CreateOrderHandler>();
services.AddTransient<IValidator<CreateOrderCommand>, CreateOrderCommandValidator>();
```

### `--type query --result`
```csharp
public sealed record GetUserByIdQuery(Guid UserId)
    : IQuery<Result<UserDto>>, ICacheableQuery  // si --behaviors cache
{
    public string CacheKey => $"user:{UserId}";
    public TimeSpan? Expiry => TimeSpan.FromMinutes(5);
}

public sealed class GetUserByIdHandler(IUserReadRepository readRepo)
    : IQueryHandler<GetUserByIdQuery, Result<UserDto>>
{
    public async ValueTask<Result<UserDto>> Handle(
        GetUserByIdQuery query,
        CancellationToken ct = default)
    {
        // TODO: implement
        throw new NotImplementedException();
    }
}
```

### `--type event`
```csharp
// Domain Event (pur domaine)
public sealed record UserRegisteredEvent(
    Guid UserId,
    string Email,
    DateTimeOffset RegisteredAt);

// Notification MediatR
public sealed class UserRegisteredNotification
    : DomainEventNotification<UserRegisteredEvent>
{
    public UserRegisteredNotification(UserRegisteredEvent domainEvent) : base(domainEvent) { }
}

// Handler
public sealed class UserRegisteredHandler
    : IDomainEventHandler<UserRegisteredEvent, UserRegisteredNotification>
{
    public async Task Handle(
        UserRegisteredNotification notification,
        CancellationToken cancellationToken)
    {
        // TODO: implement
        throw new NotImplementedException();
    }
}
```

### `--type stream`
```csharp
public sealed record GetProductsFeedQuery(string Category)
    : IStreamQuery<ProductDto>;

public sealed class GetProductsFeedHandler(IProductReadRepository repo)
    : IStreamQueryHandler<GetProductsFeedQuery, ProductDto>
{
    public async IAsyncEnumerable<ProductDto> Handle(
        GetProductsFeedQuery query,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var product in repo.StreamByCategoryAsync(query.Category, ct))
            yield return product.ToDto();
    }
}
```

## Règles appliquées automatiquement
1. `sealed record` pour les commandes/queries
2. `sealed class` pour les handlers
3. `ValueTask<T>` pour les handlers async (Command/Query)
4. `CancellationToken` toujours en dernier avec `default`
5. Markers d'interface appliqués selon `--behaviors`
6. XML docs générés sur les contrats publics
7. Test skeleton généré dans le projet de tests correspondant

## Output (fichiers créés)
- `Commands/{HandlerName}Command.cs` ou `Queries/{HandlerName}Query.cs`
- `Commands/{HandlerName}CommandValidator.cs` (si --behaviors validation)
- `Commands/Handlers/{HandlerName}Handler.cs`
- `tests/.../Commands/{HandlerName}HandlerTests.cs`
