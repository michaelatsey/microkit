namespace MicroKit.Cqrs.Abstractions.Commands;

// Interface marqueur pour identifier une commande
public interface ICommand { }

// Pour les commandes retournant une réponse (ex: un ID ou un Result)
public interface ICommand<out TResponse> : ICommand { }


