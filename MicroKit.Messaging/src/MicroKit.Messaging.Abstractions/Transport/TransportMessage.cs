using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Messaging.Abstractions.Transport;

/// <summary>
/// Réprésente un message de transport utilisé pour l'envoi et la réception de messages via le système de messagerie.
/// </summary>
public class TransportMessage
{
    /// <summary>
    /// Gets or sets the identifier.
    /// </summary>
    /// <value>
    /// The identifier.
    /// </value>
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>
    /// Gets or sets the destination.
    /// </summary>
    /// <value>
    /// The destination.
    /// </value>
    public string Destination { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the type of the message.
    /// </summary>
    /// <value>
    /// The type of the message.
    /// </value>
    public string MessageType { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the payload.
    /// </summary>
    /// <value>
    /// The payload.
    /// </value>
    public string Payload { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the correlation identifier.
    /// </summary>
    /// <value>
    /// The correlation identifier.
    /// </value>
    public string? CorrelationId { get; set; }
    /// <summary>
    /// Gets or sets the properties.
    /// </summary>
    /// <value>
    /// The properties.
    /// </value>
    public Dictionary<string, string> Properties { get; set; } = new();
    /// <summary>
    /// Gets or sets the created at.
    /// </summary>
    /// <value>
    /// The created at.
    /// </value>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
