using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace MicroKit.Persistence.IntegrationTests.EntityFrameworkCore;

/// <summary>A test context whose model depends on a variant (a misuse, a shape): one model per variant.</summary>
public interface IModelVariant
{
    /// <summary>Gets the variant the model is built for.</summary>
    object Variant { get; }
}

/// <summary>
/// The one model cache key factory every variant context replaces EF Core's with. Shared on purpose:
/// EF Core builds an internal service provider per distinct set of replaced services, and throws
/// <c>ManyServiceProvidersCreatedWarning</c> past twenty in one process — a factory per context type
/// made the suite cross that line.
/// </summary>
public sealed class ModelVariantCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        context is IModelVariant variant
            ? (context.GetType(), variant.Variant, designTime)
            : (context.GetType(), designTime);

    /// <summary>Replaces EF Core's model cache key factory with this one.</summary>
    public static void Use(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.ReplaceService<IModelCacheKeyFactory, ModelVariantCacheKeyFactory>();
}
