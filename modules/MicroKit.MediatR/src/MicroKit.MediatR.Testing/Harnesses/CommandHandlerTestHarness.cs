namespace MicroKit.MediatR.Testing;

/// <summary>
/// Test harness for <see cref="ICommandHandler{TCommand,TResult}"/> implementations.
/// Executes the handler in isolation — no DI container, no real <c>IMediator</c>.
/// </summary>
/// <typeparam name="TCommand">The command type, must implement <see cref="ICommand{TResult}"/>.</typeparam>
/// <typeparam name="TResult">The result type returned by the command handler.</typeparam>
/// <remarks>
/// Use the factory constructor when the handler publishes domain events so the harness can
/// track them via an internal <see cref="FakeDomainEventDispatcher"/>:
/// <code>
/// var harness = new CommandHandlerTestHarness&lt;CreateOrderCommand, Result&lt;OrderId&gt;&gt;(
///     dispatcher => new CreateOrderHandler(mockRepo, dispatcher));
///
/// var result = await harness.SendAsync(new CreateOrderCommand(...));
/// result.IsSuccess.ShouldBeTrue();
/// harness.AssertEventPublished&lt;OrderCreatedEvent&gt;();
/// </code>
/// Use the direct constructor when the handler does not publish domain events:
/// <code>
/// var harness = new CommandHandlerTestHarness&lt;UpdateCacheCommand, Result&lt;Unit&gt;&gt;(
///     new UpdateCacheHandler(mockCache));
/// </code>
/// </remarks>
public sealed class CommandHandlerTestHarness<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    private readonly ICommandHandler<TCommand, TResult> _handler;
    private readonly FakeDomainEventDispatcher _dispatcher;

    /// <summary>
    /// Initialises the harness using a factory that receives the internal
    /// <see cref="FakeDomainEventDispatcher"/>. Use this constructor for handlers that
    /// publish domain events — the dispatcher is wired automatically so
    /// <see cref="AssertEventPublished{TEvent}"/> works on the harness.
    /// </summary>
    /// <param name="factory">
    /// A factory that receives the harness-managed <see cref="IDomainEventDispatcher"/>
    /// and returns the constructed handler.
    /// </param>
    public CommandHandlerTestHarness(Func<IDomainEventDispatcher, ICommandHandler<TCommand, TResult>> factory)
    {
        _dispatcher = new FakeDomainEventDispatcher();
        _handler = factory(_dispatcher);
    }

    /// <summary>
    /// Initialises the harness with a pre-constructed handler.
    /// Use this constructor for handlers that do not publish domain events.
    /// </summary>
    /// <param name="handler">The handler instance to test.</param>
    /// <remarks>
    /// When using this constructor the harness cannot observe domain events — the handler
    /// was constructed independently and may hold a different <see cref="IDomainEventDispatcher"/>
    /// instance. Calling <see cref="AssertEventPublished{TEvent}"/> will always fail because
    /// the harness's internal dispatcher never receives events. Use the factory constructor
    /// to enable domain-event assertions.
    /// </remarks>
    public CommandHandlerTestHarness(ICommandHandler<TCommand, TResult> handler)
    {
        _dispatcher = new FakeDomainEventDispatcher();
        _handler = handler;
    }

    /// <summary>Gets all domain events published during the last <see cref="SendAsync"/> call.</summary>
    public IReadOnlyList<IEvent> PublishedEvents => _dispatcher.PublishedEvents;

    /// <summary>
    /// Sends <paramref name="command"/> to the handler and returns the result.
    /// </summary>
    /// <param name="command">The command to execute.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>The result produced by the handler.</returns>
    public async Task<TResult> SendAsync(TCommand command, CancellationToken ct = default)
        => await _handler.Handle(command, ct).ConfigureAwait(false);

    /// <summary>
    /// Asserts that at least one event of type <typeparamref name="TEvent"/> was published
    /// during the last <see cref="SendAsync"/> call.
    /// </summary>
    /// <typeparam name="TEvent">The expected domain event type.</typeparam>
    public void AssertEventPublished<TEvent>() where TEvent : IEvent =>
        _dispatcher.AssertEventPublished<TEvent>();

    /// <summary>
    /// Asserts that no domain events of any type were published during the last
    /// <see cref="SendAsync"/> call.
    /// </summary>
    public void AssertNoEventsPublished() =>
        _dispatcher.AssertNoEventsPublished();

    /// <summary>
    /// Returns the single published event of type <typeparamref name="TEvent"/>.
    /// Fails the test if zero or more than one such event was published.
    /// </summary>
    /// <typeparam name="TEvent">The expected domain event type.</typeparam>
    public TEvent GetSinglePublishedEvent<TEvent>() where TEvent : IEvent =>
        _dispatcher.GetSinglePublishedEvent<TEvent>();
}

/// <summary>
/// Test harness for <see cref="ICommandHandler{TCommand}"/> implementations (void commands).
/// Executes the handler in isolation — no DI container, no real <c>IMediator</c>.
/// </summary>
/// <typeparam name="TCommand">The command type, must implement <see cref="ICommand"/>.</typeparam>
/// <remarks>
/// Use the factory constructor for handlers that publish domain events:
/// <code>
/// var harness = new CommandHandlerTestHarness&lt;DeleteUserCommand&gt;(
///     dispatcher => new DeleteUserHandler(mockRepo, dispatcher));
///
/// await harness.SendAsync(new DeleteUserCommand(userId));
/// harness.AssertEventPublished&lt;UserDeletedEvent&gt;();
/// </code>
/// </remarks>
public sealed class CommandHandlerTestHarness<TCommand>
    where TCommand : ICommand
{
    private readonly ICommandHandler<TCommand> _handler;
    private readonly FakeDomainEventDispatcher _dispatcher;

    /// <summary>
    /// Initialises the harness using a factory that receives the internal
    /// <see cref="FakeDomainEventDispatcher"/>.
    /// </summary>
    /// <param name="factory">
    /// A factory that receives the harness-managed <see cref="IDomainEventDispatcher"/>
    /// and returns the constructed handler.
    /// </param>
    public CommandHandlerTestHarness(Func<IDomainEventDispatcher, ICommandHandler<TCommand>> factory)
    {
        _dispatcher = new FakeDomainEventDispatcher();
        _handler = factory(_dispatcher);
    }

    /// <summary>
    /// Initialises the harness with a pre-constructed handler.
    /// Use this constructor for handlers that do not publish domain events.
    /// </summary>
    /// <param name="handler">The handler instance to test.</param>
    /// <remarks>
    /// When using this constructor the harness cannot observe domain events — the handler
    /// was constructed independently and may hold a different <see cref="IDomainEventDispatcher"/>
    /// instance. Calling <see cref="AssertEventPublished{TEvent}"/> will always fail because
    /// the harness's internal dispatcher never receives events. Use the factory constructor
    /// to enable domain-event assertions.
    /// </remarks>
    public CommandHandlerTestHarness(ICommandHandler<TCommand> handler)
    {
        _dispatcher = new FakeDomainEventDispatcher();
        _handler = handler;
    }

    /// <summary>Gets all domain events published during the last <see cref="SendAsync"/> call.</summary>
    public IReadOnlyList<IEvent> PublishedEvents => _dispatcher.PublishedEvents;

    /// <summary>
    /// Sends <paramref name="command"/> to the handler.
    /// </summary>
    /// <param name="command">The command to execute.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    public async Task SendAsync(TCommand command, CancellationToken ct = default)
        => await _handler.Handle(command, ct).ConfigureAwait(false);

    /// <summary>
    /// Asserts that at least one event of type <typeparamref name="TEvent"/> was published
    /// during the last <see cref="SendAsync"/> call.
    /// </summary>
    /// <typeparam name="TEvent">The expected domain event type.</typeparam>
    public void AssertEventPublished<TEvent>() where TEvent : IEvent =>
        _dispatcher.AssertEventPublished<TEvent>();

    /// <summary>
    /// Asserts that no domain events of any type were published during the last
    /// <see cref="SendAsync"/> call.
    /// </summary>
    public void AssertNoEventsPublished() =>
        _dispatcher.AssertNoEventsPublished();

    /// <summary>
    /// Returns the single published event of type <typeparamref name="TEvent"/>.
    /// Fails the test if zero or more than one such event was published.
    /// </summary>
    /// <typeparam name="TEvent">The expected domain event type.</typeparam>
    public TEvent GetSinglePublishedEvent<TEvent>() where TEvent : IEvent =>
        _dispatcher.GetSinglePublishedEvent<TEvent>();
}
