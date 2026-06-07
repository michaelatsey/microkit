namespace MicroKit.Auth.AspNetCore;

/// <summary>
/// DI registration extensions for <c>MicroKit.Auth.AspNetCore</c>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all MicroKit.Auth services including ASP.NET Core authorization infrastructure:
    /// Core security context services, <see cref="PermissionAuthorizationHandler"/>, and
    /// <see cref="PermissionPolicyProvider"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Internally calls <c>AddMicroKitAuthCore()</c> — do not call both.
    /// </para>
    /// <para>
    /// Calls <c>AddAuthorization()</c> so consumers only need to configure it optionally.
    /// ASP.NET Core's <c>AddAuthorization</c> is idempotent.
    /// </para>
    /// <para>
    /// Calls <c>AddHttpContextAccessor()</c> required by <see cref="PermissionAuthorizationHandler"/>
    /// to resolve scoped services per request without a captive dependency.
    /// </para>
    /// <para>
    /// Does <b>not</b> configure an authentication scheme. Add JWT Bearer, Cookie, or a provider
    /// package (e.g. <c>MicroKit.Auth.Supabase</c>) separately via
    /// <c>services.AddAuthentication().AddJwtBearer(...)</c>.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configure">Optional builder configuration action.</param>
    /// <returns>A <see cref="MicroKitAuthBuilder"/> for chaining provider or feature registrations.</returns>
    public static MicroKitAuthBuilder AddMicroKitAuth(
        this IServiceCollection services,
        Action<MicroKitAuthBuilder>? configure = null)
    {
        // Core: ICurrentUserAccessor, ISecurityContext, PermissionEvaluator, IClaimsMapper, IPermissionStore
        services.AddMicroKitAuthCore();

        // Required by PermissionAuthorizationHandler to resolve scoped IPermissionChecker per-request
        services.AddHttpContextAccessor();

        // Authorization infrastructure — must come before PermissionPolicyProvider registration
        services.AddAuthorization();

        // AddSingleton (non-Try) is intentional: AddAuthorization() registers
        // DefaultAuthorizationPolicyProvider via TryAdd first; only a non-Try
        // registration overrides it at resolution time.
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

        // Singleton: safe because IPermissionChecker is resolved per-call via IHttpContextAccessor
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

        var builder = new MicroKitAuthBuilder(services);
        configure?.Invoke(builder);
        return builder;
    }
}
