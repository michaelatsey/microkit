using MicroKit.Idempotency.Abstractions.Contracts;
using MicroKit.Idempotency.Core.Configuration;
using MicroKit.Idempotency.Core.Persistence;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroKit.Idempotency.Core.Extensions;

public static class MicroKitIdempotencyPersistenceExtensions
{
    public static MicroKitIdempotencyBuilder UseIndempotencyImemoryStore(this MicroKitIdempotencyBuilder builder)
    {
        builder.Services.TryAddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        return builder;
    }
}
