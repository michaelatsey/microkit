namespace MicroKit.MediatR.Testing;

/// <summary>
/// Test harness for <see cref="ICommandHandler{TCommand,TResult}"/> implementations.
/// Executes the handler in isolation — no DI container, no real <c>IMediator</c>.
/// </summary>
/// <typeparam name="TCommand">The command type, must implement <see cref="ICommand{TResult}"/>.</typeparam>
/// <typeparam name="TResult">The result type returned by the command handler.</typeparam>
/// <remarks>
/// Use the factory constructor when the handler takes an <see cref="IDomainEventsDispatcher"/>
/// dependency (legacy pattern). With the current domain-event design, handlers accumulate events
/// on aggregates rather than calling the dispatcher directly — the dispatcher is invoked by
/// <c>TransactionBehavior</c>. For handler-only unit tests, use the direct constructor.
/// <code>
/// // Direct constructor — handler does not inject IDomainEventsDispatcher
/// var harness = new CommandHandlerTestHarness&lt;CreateOrderCommand, Result&lt;OrderId&gt;&gt;(
///     new CreateOrderHandler(mockRepo));
///
/// var result = await harness.SendAsync(new CreateOrderCommand(...));
/// result.IsSuccess.ShouldBeTrue();
/// </code>
/// </remarks>
public sealed class CommandHandlerTestHarness<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    private readonly ICommandHandler<TCommand, TResult> _handler;
    private readonly FakeDomainEventDispatcher _dispatcher;

    /// <summary>
    /// Initialises the harness using a factory that receives a <see cref="FakeDomainEventDispatcher"/>.
    /// Use this constructor for handlers that still accept <see cref="IDomainEventsDispatcher"/>
    /// as a constructor parameter.
    /// </summary>
    /// <param name="factory">
    /// A factory that receives the harness-managed <see cref="IDomainEventsDispatcher"/>
    /// and returns the constructed handler.
    /// </param>
    public CommandHandlerTestHarness(Func<IDomainEventsDispatcher, ICommandHandler<TCommand, TResult>> factory)
    {
        _dispatcher = new FakeDomainEventDispatcher();
        _handler = factory(_dispatcher);
    }

    /// <summary>
    /// Initialises the harness with a pre-constructed handler.
    /// </summary>
    /// <param name="handler">The handler instance to test.</param>
    public CommandHandlerTestHarness(ICommandHandler<TCommand, TResult> handler)
    {
        _dispatcher = new FakeDomainEventDispatcher();
        _handler = handler;
    }

    /// <summary>
    /// Gets the internal <see cref="FakeDomainEventDispatcher"/> used by this harness.
    /// Useful for asserting that <c>DispatchEventsAsync</c> was (or was not) called.
    /// </summary>
    public FakeDomainEventDispatcher Dispatcher => _dispatcher;

    /// <summary>
    /// Sends <paramref name="command"/> to the handler and returns the result.
    /// </summary>
    /// <param name="command">The command to execute.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>The result produced by the handler.</returns>
    public async Task<TResult> SendAsync(TCommand command, CancellationToken ct = default)
        => await _handler.Handle(command, ct).ConfigureAwait(false);
}

/// <summary>
/// Test harness for <see cref="ICommandHandler{TCommand}"/> implementations (void commands).
/// Executes the handler in isolation — no DI container, no real <c>IMediator</c>.
/// </summary>
/// <typeparam name="TCommand">The command type, must implement <see cref="ICommand"/>.</typeparam>
/// <remarks>
/// Use the factory constructor for handlers that accept <see cref="IDomainEventsDispatcher"/>
/// as a constructor parameter. For handlers that accumulate events on aggregates, use the
/// direct constructor instead.
/// </remarks>
public sealed class CommandHandlerTestHarness<TCommand>
    where TCommand : ICommand
{
    private readonly ICommandHandler<TCommand> _handler;
    private readonly FakeDomainEventDispatcher _dispatcher;

    /// <summary>
    /// Initialises the harness using a factory that receives a <see cref="FakeDomainEventDispatcher"/>.
    /// </summary>
    /// <param name="factory">
    /// A factory that receives the harness-managed <see cref="IDomainEventsDispatcher"/>
    /// and returns the constructed handler.
    /// </param>
    public CommandHandlerTestHarness(Func<IDomainEventsDispatcher, ICommandHandler<TCommand>> factory)
    {
        _dispatcher = new FakeDomainEventDispatcher();
        _handler = factory(_dispatcher);
    }

    /// <summary>
    /// Initialises the harness with a pre-constructed handler.
    /// </summary>
    /// <param name="handler">The handler instance to test.</param>
    public CommandHandlerTestHarness(ICommandHandler<TCommand> handler)
    {
        _dispatcher = new FakeDomainEventDispatcher();
        _handler = handler;
    }

    /// <summary>
    /// Gets the internal <see cref="FakeDomainEventDispatcher"/> used by this harness.
    /// </summary>
    public FakeDomainEventDispatcher Dispatcher => _dispatcher;

    /// <summary>
    /// Sends <paramref name="command"/> to the handler.
    /// </summary>
    /// <param name="command">The command to execute.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    public async Task SendAsync(TCommand command, CancellationToken ct = default)
        => await _handler.Handle(command, ct).ConfigureAwait(false);
}
