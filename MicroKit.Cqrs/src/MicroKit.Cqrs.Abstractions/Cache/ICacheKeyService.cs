using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Cqrs.Abstractions.Cache;

/// <summary>Builds normalized cache keys for CQRS query results.</summary>
public interface ICacheKeyService
{
    /// <summary>Builds a fully-qualified cache key from the given custom key segment.</summary>
    /// <param name="customKey">The request-specific key fragment.</param>
    /// <returns>The normalized, fully-qualified cache key.</returns>
    string BuildKey(string customKey);
}
