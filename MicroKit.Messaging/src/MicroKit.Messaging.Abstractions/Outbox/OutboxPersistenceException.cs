using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Messaging.Abstractions.Outbox
{
    [Serializable]
    public class OutboxPersistenceException : Exception
    {
        public string? EntityId { get; }

        public OutboxPersistenceException() : base("Une erreur est survenue lors de l'accès à la persistance de l'Outbox.") { }

        public OutboxPersistenceException(string message) : base(message) { }

        public OutboxPersistenceException(string message, Exception innerException)
            : base(message, innerException) { }

        public OutboxPersistenceException(string message, string entityId, Exception innerException)
            : base($"{message} (Id: {entityId})", innerException)
        {
            EntityId = entityId;
        }
    }
}
