using MicroKit.Idempotency.Abstractions.Contracts;
using MicroKit.Idempotency.Core.Configuration;
using MicroKit.Idempotency.Core.Persistence;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroKit.Idempotency.Core.Extensions;

/// <summary>Extension methods for registering idempotency persistence backends.</summary>
public static class MicroKitIdempotencyPersistenceExtensions
{
    /// <summary>Registers the in-memory <see cref="IIdempotencyStore"/> (suitable for development and testing only).</summary>
    public static MicroKitIdempotencyBuilder UseIndempotencyImemoryStore(this MicroKitIdempotencyBuilder builder)
    {
        builder.Services.TryAddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        return builder;
    }
}
