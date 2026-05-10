using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Messaging.Abstractions.Common
{
    public sealed class EventEnvelope<T>
    {
        public required string EventId { get; init; }
        public required string TenantId { get; init; } // Ajouté pour le destinataire
        public required string MessageType { get; init; }
        public required T Payload { get; init; }

        public DateTimeOffset OccurredOnUtc { get; init; }
        public DateTimeOffset PublishedAtUtc { get; init; }

        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
        public string? IdempotencyKey { get; init; }

        public Dictionary<string, string>? Metadata { get; init; }
    }
}
