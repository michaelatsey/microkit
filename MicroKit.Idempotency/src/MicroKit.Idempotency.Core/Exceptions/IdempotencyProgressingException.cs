using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Idempotency.Core.Exceptions;

/// <summary>Thrown when a concurrent request with the same idempotency key is already being processed.</summary>
public class IdempotencyProgressingException : Exception
{
    /// <summary>Gets the idempotency key that is already in progress.</summary>
    public string IdempotencyKey { get; }

    /// <summary>Initializes a new instance for the specified in-progress key.</summary>
    /// <param name="key">The idempotency key already being processed.</param>
    public IdempotencyProgressingException(string key)
        : base($"An operation with idempotency key '{key}' is already in progress")
    {
        IdempotencyKey = key;
    }
}
