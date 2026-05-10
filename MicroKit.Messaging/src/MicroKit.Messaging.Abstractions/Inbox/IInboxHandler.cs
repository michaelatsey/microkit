using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Messaging.Abstractions.Inbox;

public interface IInboxHandler<in TMessage> where TMessage : class
{
    /// <summary>
    /// Traite un message provenant de l'Inbox.
    /// </summary>
    /// <param name="message">Le contenu du message désérialisé.</param>
    /// <param name="ct">Token d'annulation.</param>
    Task HandleAsync(TMessage message, CancellationToken ct = default);
}
