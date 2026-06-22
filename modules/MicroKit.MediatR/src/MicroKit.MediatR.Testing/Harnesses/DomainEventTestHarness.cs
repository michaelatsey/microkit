namespace MicroKit.MediatR.Testing;

/// <summary>
/// Test harness for <see cref="IDomainEventHandler{TEvent}"/> implementations.
/// Executes the handler in isolation — no DI container, no real dispatch pipeline.
/// </summary>
/// <typeparam name="TEvent">The domain event type.</typeparam>
/// <example>
/// <code>
/// private readonly IEmailService _email = Substitute.For&lt;IEmailService&gt;();
/// private readonly DomainEventTestHarness&lt;UserRegisteredEvent&gt; _harness;
///
/// public SendWelcomeEmailHandlerTests()
///     => _harness = new(new SendWelcomeEmailHandler(_email));
///
/// [Fact]
/// public async Task Handle_WhenUserRegistered_SendsWelcomeEmail()
/// {
///     var domainEvent = new UserRegisteredEvent(userId, "user@example.com", DateTimeOffset.UtcNow);
///
///     await _harness.HandleAsync(domainEvent);
///
///     await _email.Received(1).SendWelcomeAsync("user@example.com", Arg.Any&lt;CancellationToken&gt;());
/// }
/// </code>
/// </example>
public sealed class DomainEventTestHarness<TEvent>
    where TEvent : MicroKit.Domain.Events.IDomainEvent
{
    private readonly IDomainEventHandler<TEvent> _handler;

    /// <summary>
    /// Initialises the harness with the handler under test.
    /// </summary>
    /// <param name="handler">The domain event handler instance to test.</param>
    public DomainEventTestHarness(IDomainEventHandler<TEvent> handler) =>
        _handler = handler;

    /// <summary>
    /// Delivers <paramref name="domainEvent"/> directly to the handler.
    /// </summary>
    /// <param name="domainEvent">The domain event to handle.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    public async Task HandleAsync(TEvent domainEvent, CancellationToken ct = default)
        => await _handler.Handle(domainEvent, ct).ConfigureAwait(false);
}
