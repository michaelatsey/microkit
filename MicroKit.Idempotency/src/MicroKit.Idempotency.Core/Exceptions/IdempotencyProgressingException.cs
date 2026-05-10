using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Idempotency.Core.Exceptions;

public class IdempotencyProgressingException : Exception
{
    public string IdempotencyKey { get; }

    public IdempotencyProgressingException(string key)
        : base($"An operation with idempotency key '{key}' is already in progress")
    {
        IdempotencyKey = key;
    }
}
