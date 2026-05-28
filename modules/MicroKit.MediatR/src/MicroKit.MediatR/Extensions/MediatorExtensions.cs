namespace MicroKit.MediatR;

/// <summary>
/// Typed dispatch extensions on <see cref="IMediator"/> for MicroKit CQRS contracts.
/// Prefer these over <c>IMediator.Send</c> directly — they preserve CQRS semantics at the call site.
/// </summary>
public static class MediatorExtensions
{
    /// <summary>Sends a void command through the MediatR pipeline.</summary>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <param name="mediator">The MediatR mediator.</param>
    /// <param name="command">The command to send.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    public static ValueTask SendCommandAsync<TCommand>(
        this IMediator mediator,
        TCommand command,
        CancellationToken ct = default)
        where TCommand : ICommand
        => new(mediator.Send(command, ct));

    /// <summary>Sends a result-bearing command through the MediatR pipeline.</summary>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="mediator">The MediatR mediator.</param>
    /// <param name="command">The command to send.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>The command result.</returns>
    public static ValueTask<TResult> SendCommandAsync<TCommand, TResult>(
        this IMediator mediator,
        TCommand command,
        CancellationToken ct = default)
        where TCommand : ICommand<TResult>
        => new(mediator.Send<TResult>(command, ct));

    /// <summary>Sends a query through the MediatR pipeline.</summary>
    /// <typeparam name="TQuery">The query type.</typeparam>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="mediator">The MediatR mediator.</param>
    /// <param name="query">The query to send.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>The query result.</returns>
    public static ValueTask<TResult> SendQueryAsync<TQuery, TResult>(
        this IMediator mediator,
        TQuery query,
        CancellationToken ct = default)
        where TQuery : IQuery<TResult>
        => new(mediator.Send<TResult>(query, ct));

    /// <summary>Streams query results through the MediatR pipeline.</summary>
    /// <typeparam name="TQuery">The stream query type.</typeparam>
    /// <typeparam name="TResult">The item type.</typeparam>
    /// <param name="mediator">The MediatR mediator.</param>
    /// <param name="query">The stream query to send.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>An async sequence of <typeparamref name="TResult"/> items.</returns>
    public static IAsyncEnumerable<TResult> StreamQueryAsync<TQuery, TResult>(
        this IMediator mediator,
        TQuery query,
        CancellationToken ct = default)
        where TQuery : IStreamQuery<TResult>
        => mediator.CreateStream<TResult>(query, ct);
}
