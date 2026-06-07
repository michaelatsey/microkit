using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroKit.Auth;

/// <summary>
/// DI registration extensions for <c>MicroKit.Auth</c> Core.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Core MicroKit.Auth services: <see cref="ICurrentUserAccessor"/>,
    /// <see cref="ISecurityContext"/>, <see cref="IPermissionChecker"/>,
    /// <see cref="ITenantPermissionChecker"/>, <see cref="IPermissionStore"/>, and
    /// <see cref="IClaimsMapper"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All registrations use <c>TryAdd</c> variants so consumers and provider packages can
    /// override any component by registering their own implementation before or after this call.
    /// </para>
    /// <para>
    /// <see cref="IPermissionStore"/> is registered with <see cref="NullPermissionStore"/> by
    /// default. Replace it with your infrastructure store (e.g. EF Core, Redis) by calling
    /// <c>services.AddScoped&lt;IPermissionStore, MyStore&gt;()</c> after this method.
    /// </para>
    /// <para>
    /// <see cref="ICurrentUserAccessor"/> is registered as <b>Scoped</b>. Never override with
    /// a Singleton — the underlying <see cref="System.Threading.AsyncLocal{T}"/> is host-agnostic
    /// and designed for per-request isolation.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddMicroKitAuthCore(this IServiceCollection services)
    {
        // Scoped — never Singleton; AsyncLocal provides per-request isolation
        services.TryAddScoped<ICurrentUserAccessor, CurrentUserAccessor>();

        // Scoped — reads from ICurrentUserAccessor
        services.TryAddScoped<ISecurityContext, SecurityContext>();

        // Concrete type registered once; both interfaces delegate to the same scoped instance
        services.TryAddScoped<PermissionEvaluator>();
        services.TryAddScoped<IPermissionChecker>(
            sp => sp.GetRequiredService<PermissionEvaluator>());
        services.TryAddScoped<ITenantPermissionChecker>(
            sp => sp.GetRequiredService<PermissionEvaluator>());

        // Null store — denies all non-SuperAdmin checks until replaced by consumer
        services.TryAddScoped<IPermissionStore, NullPermissionStore>();

        // Default claims mapper — stateless, overridable by provider packages (Supabase, etc.)
        services.TryAddSingleton<IClaimsMapper, ClaimsMapper>();

        return services;
    }
}
