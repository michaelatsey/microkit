using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Messaging.Abstractions.Inbox;

/// <summary>Handles a strongly-typed message dispatched from the inbox processor.</summary>
/// <typeparam name="TMessage">The CLR type of the deserialized message payload.</typeparam>
public interface IInboxHandler<in TMessage> where TMessage : class
{
    /// <summary>Processes an inbox message payload.</summary>
    /// <param name="message">The deserialized message content.</param>
    /// <param name="ct">Cancellation token.</param>
    Task HandleAsync(TMessage message, CancellationToken ct = default);
}
