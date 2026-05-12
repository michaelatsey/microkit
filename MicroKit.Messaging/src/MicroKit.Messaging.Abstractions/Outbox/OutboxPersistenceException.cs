using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Messaging.Abstractions.Outbox
{
    /// <summary>Thrown when an outbox persistence operation fails (e.g. a database write or read error).</summary>
    [Serializable]
    public class OutboxPersistenceException : Exception
    {
        /// <summary>Gets the identifier of the entity that caused the failure, if available.</summary>
        public string? EntityId { get; }

        /// <summary>Initializes a new instance with a default error message.</summary>
        public OutboxPersistenceException() : base("Une erreur est survenue lors de l'accès à la persistance de l'Outbox.") { }

        /// <summary>Initializes a new instance with the specified error message.</summary>
        /// <param name="message">The error message.</param>
        public OutboxPersistenceException(string message) : base(message) { }

        /// <summary>Initializes a new instance with the specified error message and inner exception.</summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The exception that caused this failure.</param>
        public OutboxPersistenceException(string message, Exception innerException)
            : base(message, innerException) { }

        /// <summary>Initializes a new instance with the specified error message, entity ID, and inner exception.</summary>
        /// <param name="message">The error message.</param>
        /// <param name="entityId">The identifier of the entity involved in the failure.</param>
        /// <param name="innerException">The exception that caused this failure.</param>
        public OutboxPersistenceException(string message, string entityId, Exception innerException)
            : base($"{message} (Id: {entityId})", innerException)
        {
            EntityId = entityId;
        }
    }
}
