using MicroKit.Idempotency.Abstractions.Contracts;
using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Idempotency.Core.Accessors;

public class IdempotencyProvider : IIdempotencyManager
{
    // On utilise un champ simple, pas de nettoyage dans un Dispose.
    // L'objet étant Scoped, il mourra avec la fin de la requête/message.
    public string? CurrentKey { get; private set; }

    public void SetKey(string key)
    {
        if (CurrentKey != null && CurrentKey != key)
            throw new InvalidOperationException("Idempotency key already set for this scope.");

        CurrentKey = key;
    }

    public void Clear() => CurrentKey = null;
}
