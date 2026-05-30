namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// Builder for configuring EF Core-specific persistence services.
/// </summary>
/// <remarks>
/// Obtain an instance via
/// <see cref="PersistenceServiceCollectionExtensions.AddEntityFrameworkCore"/>.
/// Provider-specific extension methods (<c>UsePostgreSQL</c>, <c>UseSqlServer</c>) are defined
/// in the respective provider packages.
/// </remarks>
public sealed class EfCoreBuilder
{
    internal EfCoreBuilder(PersistenceBuilder parent) => Persistence = parent;

    /// <summary>The parent <see cref="PersistenceBuilder"/>.</summary>
    public PersistenceBuilder Persistence { get; }

    /// <summary>
    /// The underlying <see cref="IServiceCollection"/>.
    /// Shortcut for <c>Persistence.Services</c>.
    /// </summary>
    public IServiceCollection Services => Persistence.Services;
}
