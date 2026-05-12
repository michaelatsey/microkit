using MediatR;
using MicroKit.Messaging.Abstractions.Common;
using MicroKit.Messaging.Abstractions.Inbox;
using System.Text.Json;
using System.Windows.Input;

namespace MicroKit.Messaging.Publisher.MediatR;



/// <summary>MediatR implementation of <see cref="IInboxPublisher"/> that dispatches inbox contexts as MediatR commands or notifications.</summary>
public class MediatRInboxPublisher : IInboxPublisher
{
    private readonly IPublisher _publisher;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="publisher">The MediatR publisher used to dispatch commands and notifications.</param>
    public MediatRInboxPublisher(IPublisher publisher)
    {
        _publisher = publisher;
    }

    /// <inheritdoc/>
    public async Task PublishAsync(InboxContext context, CancellationToken cancellationToken = default)
    {
        // Create a MediatR command based on the InboxContext
        //var type = _domainNotificationsMapper.GetType(message.Type);
        //var command = new MediatRInboxCommand
        //{
        //    MessageId = context.MessageId,
        //    MessageType = context.MessageType,
        //    Payload = context.Payload,
        //    ConsumerId = context.ConsumerId,
        //    CorrelationId = context.CorrelationId,
        //    Metadata = context.Metadata
        //};

        //var messageType = Type.GetType(message.MessageType);
        //if (messageType == null) throw new Exception($"Type inconnu : {message.MessageType}");

        //// On désérialise le payload JSON
        //var payload = JsonSerializer.Deserialize(message.Payload, messageType);

        //// On construit le type du handler dynamiquement : IInboxHandler<T>
        //var handlerType = typeof(IInboxHandler<>).MakeGenericType(messageType);

        //// On récupère le handler depuis la DI
        //var handler = _serviceProvider.GetService(handlerType);
        //if (handler == null) throw new Exception($"Aucun handler enregistré pour {messageType.Name}");

        //// Appel de la méthode "HandleAsync" via réflexion (ou via un wrapper casté)
        //var method = handlerType.GetMethod(nameof(IInboxHandler<object>.HandleAsync));
        //await (Task)method!.Invoke(handler, new[] { payload, ct })!;
        if (context is ICommand command)
        {
            await _publisher.Publish(command, cancellationToken);
        }
        else if (context is INotification notification)
        {
            // Publish appelle TOUS les handlers enregistrés pour cet événement
            await _publisher.Publish(notification, cancellationToken);
        }
        
    }
}
