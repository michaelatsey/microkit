namespace MicroKit.Cqrs.Abstractions.Commands;

/// <summary>Marker interface for commands that produce no response value.</summary>
public interface ICommand { }

/// <summary>Marker interface for commands that produce a response value of type <typeparamref name="TResponse"/>.</summary>
/// <typeparam name="TResponse">The type returned by the command handler.</typeparam>
public interface ICommand<out TResponse> : ICommand { }
