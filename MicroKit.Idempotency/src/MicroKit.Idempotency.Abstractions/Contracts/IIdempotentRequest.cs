using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Idempotency.Abstractions.Contracts;

/// <summary>Marks a request as idempotent, carrying the key and optional expiry for deduplication.</summary>
public interface IIdempotentRequest
{
    /// <summary>
    /// Gets the idempotency key.
    /// </summary>
    /// <value>
    /// The idempotency key.
    /// </value>
    string IdempotencyKey { get; }
    /// <summary>
    /// Gets the expiration time for the idempotency record
    /// </summary>
    TimeSpan? IdempotencyExpiration { get; }
}

/// <summary>
/// Represents a command that supports idempotency with a typed response
/// </summary>
/// <typeparam name="TResponse">The type of the response</typeparam>
public interface IIdempotentRequest<out TResponse> : IIdempotentRequest
{
}
