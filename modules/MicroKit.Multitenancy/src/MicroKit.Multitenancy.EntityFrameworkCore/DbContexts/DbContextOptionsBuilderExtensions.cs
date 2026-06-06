namespace MicroKit.Multitenancy.EntityFrameworkCore;

/// <summary>Extension methods for <see cref="DbContextOptionsBuilder"/> to integrate tenant stamping.</summary>
public static class DbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Adds the <see cref="TenantStampInterceptor"/> resolved from <paramref name="serviceProvider"/>
    /// to this DbContext's interceptor chain. Call this inside <c>AddDbContext((sp, options) => ...)</c>.
    /// </summary>
    /// <param name="builder">The options builder.</param>
    /// <param name="serviceProvider">
    /// The scoped service provider from the <c>AddDbContext</c> factory callback.
    /// </param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddDbContext&lt;AppDbContext&gt;((sp, options) =>
    /// {
    ///     options.UseSqlServer(connectionString);
    ///     options.AddTenantStamping(sp);
    /// });
    /// </code>
    /// </example>
    public static DbContextOptionsBuilder AddTenantStamping(
        this DbContextOptionsBuilder builder,
        IServiceProvider serviceProvider)
    {
        var interceptor = serviceProvider.GetRequiredService<TenantStampInterceptor>();
        return builder.AddInterceptors(interceptor);
    }
}
