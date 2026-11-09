using Microsoft.EntityFrameworkCore.Diagnostics;
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
///     <see cref="AggregateVersionConvention"/> and the <see cref="AggregateBoundaryConvention"/> to
///     model building.</description></item>
///   <item><description><see cref="AggregateSaveInterceptor"/>, MicroKit's single
///     <c>SaveChanges</c> entry point, as an EF Core <see cref="IInterceptor"/>.</description></item>
/// </list>
/// <para>
/// Its one option, <see cref="FailureClassifier"/>, is read from the context's own options by
/// <see cref="EfUnitOfWork{TContext}"/>, never by a service: it takes no part in
/// <see cref="ExtensionInfo.GetServiceProviderHashCode"/>, and contexts with different classifiers share
/// one internal service provider. Any option a service reads must take part in both
/// <see cref="ExtensionInfo.GetServiceProviderHashCode"/> and
/// <see cref="ExtensionInfo.ShouldUseSameServiceProvider"/>, or two contexts configured
/// differently would share one internal service provider.
/// </para>
/// </remarks>
internal sealed class MicroKitPersistenceOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public MicroKitPersistenceOptionsExtension()
    {
    }

    // Options extensions are immutable: a new classifier is a new extension.
    public MicroKitPersistenceOptionsExtension(IDatabaseFailureClassifier failureClassifier) =>
        FailureClassifier = failureClassifier;

    /// <summary>The provider's classifier of database errors, or <see langword="null"/> when none was given.</summary>
    public IDatabaseFailureClassifier? FailureClassifier { get; }

    /// <inheritdoc/>
    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    // AggregateLoadRecord is not an EF Core service, so it goes through TryAddProviderSpecificServices.
    // IConventionSetPlugin, IInterceptor and IResettableService are: they go through TryAdd, which takes their
    // lifetime (scoped) and multiplicity (one registration per implementation) from EF Core itself.
    // The IResettableService registration resolves the same scoped record, so the pool resets it.

    /// <inheritdoc/>
    public void ApplyServices(IServiceCollection services) =>
        new EntityFrameworkServicesBuilder(services)
            .TryAdd<IConventionSetPlugin, AggregateConventionSetPlugin>()
            .TryAdd<IInterceptor, AggregateSaveInterceptor>()
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
            // Not a service: contexts with different classifiers share one internal service provider.
    }
}
