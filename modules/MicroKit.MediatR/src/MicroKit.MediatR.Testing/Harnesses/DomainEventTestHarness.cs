namespace MicroKit.MediatR.Testing;

/// <summary>
/// Test harness for <see cref="IDomainEventHandler{TEvent,TNotification}"/> implementations.
/// Executes the handler in isolation — no DI container, no real MediatR publish pipeline.
/// </summary>
/// <typeparam name="TEvent">The domain event type, must implement <see cref="IEvent"/>.</typeparam>
/// <typeparam name="TNotification">
/// The notification wrapper type, must implement <see cref="IDomainEventNotification{TEvent}"/>.
/// </typeparam>
/// <example>
/// <code>
/// private readonly IEmailService _email = Substitute.For&lt;IEmailService&gt;();
/// private readonly DomainEventTestHarness&lt;UserRegisteredEvent, UserRegisteredNotification&gt; _harness;
///
/// public SendWelcomeEmailHandlerTests()
///     => _harness = new(new SendWelcomeEmailHandler(_email));
///
/// [Fact]
/// public async Task Handle_WhenUserRegistered_SendsWelcomeEmail()
/// {
///     var notification = new UserRegisteredNotification(
///         new UserRegisteredEvent(userId, "user@example.com", DateTimeOffset.UtcNow));
///
///     await _harness.HandleAsync(notification);
///
///     await _email.Received(1).SendWelcomeAsync("user@example.com", Arg.Any&lt;CancellationToken&gt;());
/// }
/// </code>
/// </example>
public sealed class DomainEventTestHarness<TEvent, TNotification>
    where TEvent : IEvent
    where TNotification : IDomainEventNotification<TEvent>
{
    private readonly IDomainEventHandler<TEvent, TNotification> _handler;

    /// <summary>
    /// Initialises the harness with the handler under test.
    /// </summary>
    /// <param name="handler">The domain event handler instance to test.</param>
    public DomainEventTestHarness(IDomainEventHandler<TEvent, TNotification> handler) =>
        _handler = handler;

    /// <summary>
    /// Delivers <paramref name="notification"/> directly to the handler.
    /// </summary>
    /// <param name="notification">The notification containing the domain event to handle.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    public async Task HandleAsync(TNotification notification, CancellationToken ct = default)
        => await _handler.Handle(notification, ct).ConfigureAwait(false);
}
