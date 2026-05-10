using MicroKit.Messaging.Abstractions.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Messaging.Core.Contexts;

internal class MicroKitMessageContext : IMicroKitMessageContext, IMicroKitMessageContextSetter
{
    public string TenantId { get; private set; } = default!;
    public string? CorrelationId { get; private set; }
    public string? CausationId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public bool IsInProcess => !string.IsNullOrEmpty(TenantId);

    public IDisposable SetContext(
        string tenantId, 
        string? correlationId = null, 
        string? causationId = null,
        string? idempotencyKey = null
        )
    {
        TenantId = tenantId;
        CorrelationId = correlationId;
        CausationId = causationId;
        IdempotencyKey = idempotencyKey;

        return new Cleanup(this); // Pour nettoyer après usage
    }

    private class Cleanup(MicroKitMessageContext context) : IDisposable
    {
        public void Dispose()
        {
            context.TenantId = null!;
            context.CorrelationId = null;
            context.CausationId = null;
            context.IdempotencyKey = null;
        }
    }
}
