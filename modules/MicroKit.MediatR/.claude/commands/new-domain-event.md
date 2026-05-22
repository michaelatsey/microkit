# Command: /new-domain-event

## Usage
```
/new-domain-event <EventName> [--fields <field1:type,field2:type>] [--handlers <HandlerName1,HandlerName2>]
```

## Description
Génère le triptyque complet pour un DomainEvent DDD :
1. Le DomainEvent pur (appartient au domaine)
2. La DomainEventNotification MediatR (appartient à l'application)
3. Les handlers de notification (un par `--handlers`)

## Exemples
```
/new-domain-event UserRegistered --fields "userId:Guid,email:string,registeredAt:DateTimeOffset" --handlers SendWelcomeEmail,CreateDefaultProfile,AuditUserCreation

/new-domain-event OrderShipped --fields "orderId:Guid,trackingNumber:string,shippedAt:DateTimeOffset"
```

## Ce qui est généré

### 1. DomainEvent (domaine pur)
```csharp
// Domain/Events/UserRegisteredEvent.cs
namespace {YourDomain}.Domain.Events;

/// <summary>
/// Raised when a new user successfully registers.
/// This is a domain fact — it has already happened.
/// </summary>
/// <param name="UserId">The unique identifier of the registered user.</param>
/// <param name="Email">The email address used for registration.</param>
/// <param name="RegisteredAt">The UTC timestamp of registration.</param>
public sealed record UserRegisteredEvent(
    Guid UserId,
    string Email,
    DateTimeOffset RegisteredAt) : IEvent;
```

### 2. Notification (application layer)
```csharp
// Application/Notifications/UserRegisteredNotification.cs
namespace {YourDomain}.Application.Notifications;

/// <summary>
/// MediatR notification wrapping <see cref="UserRegisteredEvent"/>.
/// Dispatched by <see cref="IDomainEventDispatcher"/> after the transaction commits.
/// </summary>
public sealed class UserRegisteredNotification
    : DomainEventNotification<UserRegisteredEvent>
{
    /// <inheritdoc/>
    public UserRegisteredNotification(UserRegisteredEvent domainEvent)
        : base(domainEvent) { }
}
```

### 3. Handlers (un par --handlers)
```csharp
// Application/Notifications/Handlers/SendWelcomeEmailHandler.cs
/// <summary>
/// Sends a welcome email when a new user registers.
/// </summary>
public sealed class SendWelcomeEmailHandler(IEmailService emailService)
    : IDomainEventHandler<UserRegisteredEvent, UserRegisteredNotification>
{
    /// <inheritdoc/>
    public async Task Handle(
        UserRegisteredNotification notification,
        CancellationToken cancellationToken)
    {
        var ev = notification.DomainEvent;
        // TODO: implement
        await emailService.SendWelcomeAsync(ev.Email, cancellationToken).ConfigureAwait(false);
    }
}
```

### 4. Tests (un fichier par handler)
```csharp
public sealed class SendWelcomeEmailHandlerTests
{
    private readonly IEmailService _email = Substitute.For<IEmailService>();
    private readonly DomainEventTestHarness<UserRegisteredEvent, UserRegisteredNotification> _harness;

    public SendWelcomeEmailHandlerTests()
        => _harness = new(new SendWelcomeEmailHandler(_email));

    [Fact]
    public async Task SendWelcomeEmail_WhenUserRegistered()
    {
        var notification = new UserRegisteredNotification(
            new UserRegisteredEvent(Guid.NewGuid(), "user@example.com", DateTimeOffset.UtcNow));

        await _harness.HandleAsync(notification);

        await _email.Received(1).SendWelcomeAsync("user@example.com", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotSendEmail_WhenCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        // Assert: OperationCanceledException ou no-op selon l'impl
    }
}
```

## Règles appliquées
1. DomainEvent = `sealed record` immuable, sans dépendance infrastructure
2. Notification = `sealed class` héritant `DomainEventNotification<T>`
3. Handlers = `sealed class` avec primary constructor
4. Un handler = une responsabilité (SRP strict)
5. Pas d'`IMediator` dans les handlers de domaine event
6. `ConfigureAwait(false)` sur tous les awaits
7. Tests générés pour chaque handler
