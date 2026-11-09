using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// The EF Core options extension behind <c>UseMicroKitPersistence()</c>: it adds MicroKit's
/// services to the context's internal service provider.
/// </summary>
/// <remarks>
/// <para>Registered services:</para>
/// <list type="bullet">
///   <item><description><see cref="AggregateLoadRecord"/>, scoped (one per context), also exposed as
///     <see cref="IResettableService"/> so a pooled context resets it when it returns to the
///     pool.</description></item>
///   <item><description><see cref="AggregateConventionSetPlugin"/>, which adds the
///     <see cref="AggregateBoundaryConvention"/> to model building.</description></item>
/// </list>
/// <para>
/// The extension has no options. Any option added later must take part in both
/// <see cref="ExtensionInfo.GetServiceProviderHashCode"/> and
/// <see cref="ExtensionInfo.ShouldUseSameServiceProvider"/>, or two contexts configured
/// differently would share one internal service provider.
/// </para>
/// </remarks>
internal sealed class MicroKitPersistenceOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    /// <inheritdoc/>
    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    // AggregateLoadRecord is not an EF Core service, so it goes through TryAddProviderSpecificServices.
    // IConventionSetPlugin and IResettableService are: they go through TryAdd, which takes their
    // lifetime (scoped) and multiplicity (one registration per implementation) from EF Core itself.
    // The IResettableService registration resolves the same scoped record, so the pool resets it.

    /// <inheritdoc/>
    public void ApplyServices(IServiceCollection services) =>
        new EntityFrameworkServicesBuilder(services)
            .TryAdd<IConventionSetPlugin, AggregateConventionSetPlugin>()
            .TryAdd<IResettableService, AggregateLoadRecord>(
                static provider => provider.GetRequiredService<AggregateLoadRecord>())
            .TryAddProviderSpecificServices(static map => map
                .TryAddScoped<AggregateLoadRecord, AggregateLoadRecord>());

    /// <inheritdoc/>
    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension)
        : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "using MicroKit.Persistence ";

        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) =>
            other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) =>
            debugInfo["MicroKit.Persistence:UseMicroKitPersistence"] = "1";
    }
}
