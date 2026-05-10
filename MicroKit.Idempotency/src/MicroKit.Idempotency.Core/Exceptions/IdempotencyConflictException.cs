using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Idempotency.Core.Exceptions;

public class IdempotencyConflictException : Exception
{
    public string IdempotencyKey { get; }

    public IdempotencyConflictException(string key, string message)
        : base(message)
    {
        IdempotencyKey = key;
    }
}
