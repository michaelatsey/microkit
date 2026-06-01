namespace MicroKit.MediatR;

/// <summary>
/// Marks a request that mutates state and returns no value.
/// Implement <see cref="ICommandHandler{TCommand}"/> to handle this command.
/// </summary>
public interface ICommand : IRequest;

/// <summary>
/// Marks a request that mutates state and returns <typeparamref name="TResult"/>.
/// Implement <see cref="ICommandHandler{TCommand,TResult}"/> to handle this command.
/// </summary>
/// <typeparam name="TResult">The return type. Typically <c>Result&lt;TId&gt;</c> for resource-creating commands, or <c>Result&lt;Unit&gt;</c> for update/delete commands.</typeparam>
public interface ICommand<TResult> : IRequest<TResult>;
