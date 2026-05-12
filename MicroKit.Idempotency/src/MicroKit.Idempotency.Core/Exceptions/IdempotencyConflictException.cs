using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Idempotency.Core.Exceptions;

/// <summary>Thrown when a completed idempotency record exists but conflicts with the current request.</summary>
public class IdempotencyConflictException : Exception
{
    /// <summary>Gets the idempotency key that caused the conflict.</summary>
    public string IdempotencyKey { get; }

    /// <summary>Initializes a new instance with the conflicting key and a message.</summary>
    /// <param name="key">The idempotency key.</param>
    /// <param name="message">Human-readable conflict description.</param>
    public IdempotencyConflictException(string key, string message)
        : base(message)
    {
        IdempotencyKey = key;
    }
}
