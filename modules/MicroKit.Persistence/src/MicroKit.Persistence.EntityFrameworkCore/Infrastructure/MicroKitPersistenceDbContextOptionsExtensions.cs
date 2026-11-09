using Microsoft.EntityFrameworkCore.Infrastructure;

namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// Activates MicroKit.Persistence on a <see cref="DbContext"/>'s options.
/// </summary>
public static class MicroKitPersistenceDbContextOptionsExtensions
{
    /// <summary>
    /// Adds MicroKit.Persistence to the context: the aggregate-boundary validation at model
    /// building, and the record of the roots <see cref="EfRepository{TAggregate, TId, TContext}"/>
    /// loaded whole.
    /// </summary>
    /// <param name="optionsBuilder">The context's options builder.</param>
    /// <returns><paramref name="optionsBuilder"/> for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// <c>AddDbContext</c>, <c>UsePostgreSQL</c> and <c>UseSqlServer</c> on the
    /// <see cref="EfCoreBuilder"/> call it already. Call it yourself only for a context configured
    /// another way — <c>new DbContextOptionsBuilder&lt;TContext&gt;()</c>, <c>OnConfiguring</c>, or a
    /// plain <c>services.AddDbContext</c>.
    /// </para>
    /// <para>
    /// Without it, <see cref="EfRepository{TAggregate, TId, TContext}"/> refuses to load with an
    /// <see cref="InvalidOperationException"/>. <see cref="EfReadRepository{TAggregate, TContext}"/>
    /// and <see cref="EfUnitOfWork{TContext}"/> do not need it. Calling it twice is harmless.
    /// </para>
    /// <para>
    /// The services are added to the internal service provider EF Core builds. A context given its
    /// own provider with <c>UseInternalServiceProvider</c> does not get them, and
    /// <see cref="EfRepository{TAggregate, TId, TContext}"/> reports it as not configured: that setup is
    /// not supported.
    /// </para>
    /// </remarks>
    public static DbContextOptionsBuilder UseMicroKitPersistence(this DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        var extension = optionsBuilder.Options.FindExtension<MicroKitPersistenceOptionsExtension>()
                        ?? new MicroKitPersistenceOptionsExtension();
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);
        return optionsBuilder;
    }

    /// <inheritdoc cref="UseMicroKitPersistence(DbContextOptionsBuilder)"/>
    /// <typeparam name="TContext">The application <see cref="DbContext"/> type.</typeparam>
    public static DbContextOptionsBuilder<TContext> UseMicroKitPersistence<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder)
        where TContext : DbContext =>
        (DbContextOptionsBuilder<TContext>)UseMicroKitPersistence((DbContextOptionsBuilder)optionsBuilder);
}
