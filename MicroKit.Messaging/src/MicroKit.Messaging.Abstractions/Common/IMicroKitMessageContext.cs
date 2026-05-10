namespace MicroKit.Messaging.Abstractions.Common;

public interface IMicroKitMessageContext
{
    string TenantId { get; }
    string? CorrelationId { get; }
    string? IdempotencyKey { get; }
    string? CausationId { get; }

    bool IsInProcess { get; } // Pour savoir si on est dans un flux Outbox
}

public interface IMicroKitMessageContextSetter
{
    IDisposable SetContext(
        string tenantId,
        string? correlationId = null,
        string? causationId = null,
        string? idempotencyKey = null
        );
}