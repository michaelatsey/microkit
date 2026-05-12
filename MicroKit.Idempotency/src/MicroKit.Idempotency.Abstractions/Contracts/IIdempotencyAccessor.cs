using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Idempotency.Abstractions.Contracts;

/// <summary>Provides read access to the current idempotency key for the executing operation.</summary>
public interface IIdempotencyAccessor
{
    /// <summary>Gets the idempotency key for the current operation, or <see langword="null"/> if none is active.</summary>
    string? CurrentKey { get; }

    /// <summary>Gets a value indicating whether the current operation carries a valid idempotency key.</summary>
    bool IsIdempotent => !string.IsNullOrWhiteSpace(CurrentKey);
}

/// <summary>Extends <see cref="IIdempotencyAccessor"/> with lifecycle management for use by pipeline behaviors.</summary>
public interface IIdempotencyManager : IIdempotencyAccessor
{
    /// <summary>Sets the active idempotency key for the current operation.</summary>
    /// <param name="key">The idempotency key to set.</param>
    void SetKey(string key);

    /// <summary>Clears the active idempotency key, ending the current idempotency scope.</summary>
    void Clear();
}
