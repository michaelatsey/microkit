namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// Builder for configuring <c>MicroKit.Persistence</c> services on an
/// <see cref="IServiceCollection"/>.
/// </summary>
/// <remarks>
/// Obtain an instance via
/// <see cref="PersistenceServiceCollectionExtensions.AddMicroKitPersistence"/>.
/// Use <see cref="PersistenceServiceCollectionExtensions.AddEntityFrameworkCore"/> to chain
/// EF Core-specific registrations.
/// </remarks>
public sealed class PersistenceBuilder
{
    internal PersistenceBuilder(IServiceCollection services) => Services = services;

    /// <summary>
    /// The underlying <see cref="IServiceCollection"/> for custom registrations.
    /// </summary>
    public IServiceCollection Services { get; }
}
