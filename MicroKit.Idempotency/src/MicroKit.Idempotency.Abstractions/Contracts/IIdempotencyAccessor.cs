using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Idempotency.Abstractions.Contracts;

public interface IIdempotencyAccessor
{
    string? CurrentKey { get; }
    bool IsIdempotent => !string.IsNullOrWhiteSpace(CurrentKey);
}

// Interface pour le Behavior (Ecriture et Cycle de vie)
public interface IIdempotencyManager : IIdempotencyAccessor
{
    void SetKey(string key);
    void Clear();
}
