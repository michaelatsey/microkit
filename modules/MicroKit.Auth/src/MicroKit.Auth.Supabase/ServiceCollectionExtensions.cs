using MicroKit.Auth.AspNetCore;

namespace MicroKit.Auth.Supabase;

/// <summary>
/// DI registration extensions for <c>MicroKit.Auth.Supabase</c>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all MicroKit.Auth services with Supabase ES256/JWKS JWT validation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Internally calls <c>AddMicroKitAuth()</c> — do not call both.
    /// </para>
    /// <para>
    /// <see cref="SupabaseAuthOptions"/> is validated eagerly. An
    /// <see cref="InvalidOperationException"/> is thrown immediately at startup when
    /// <see cref="SupabaseAuthOptions.ProjectUrl"/>, <see cref="SupabaseAuthOptions.Audience"/>,
    /// or <see cref="SupabaseAuthOptions.Issuer"/> are empty.
    /// </para>
    /// <para>
    /// Registers <see cref="SupabaseClaimsMapper"/> as the <see cref="IClaimsMapper"/> via
    /// <c>services.Replace()</c> per ADR-AUTH-004. The existing <c>ClaimsMapper</c> registered
    /// by <c>AddMicroKitAuth()</c> is cleanly replaced — no stale descriptor remains.
    /// </para>
    /// <para>
    /// The existing <see cref="MicroKit.Auth.AspNetCore.CurrentUserMiddleware"/> uses
    /// <see cref="IClaimsMapper"/> to populate <see cref="ICurrentUserAccessor"/> from
    /// <c>HttpContext.User</c>. Replacing the mapper is sufficient to provide Supabase-specific
    /// claim extraction — no custom accessor is required.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configure">Action that populates the <see cref="SupabaseAuthOptions"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown immediately when <see cref="SupabaseAuthOptions.ProjectUrl"/>,
    /// <see cref="SupabaseAuthOptions.Audience"/>, or <see cref="SupabaseAuthOptions.Issuer"/>
    /// are empty, or when <see cref="SupabaseAuthOptions.ProjectUrl"/> is not a valid URI.
    /// </exception>
    public static IServiceCollection AddMicroKitAuthSupabase(
        this IServiceCollection services,
        Action<SupabaseAuthOptions> configure)
    {
        var options = new SupabaseAuthOptions { ProjectUrl = string.Empty };
        configure(options);
        ValidateOptions(options);

        // Core + ASP.NET Core authorization infra (includes AddHttpContextAccessor)
        services.AddMicroKitAuth();

        services.TryAddSingleton(options);

        // Named HttpClient for JWKS endpoint fetching
        services.AddHttpClient(nameof(SupabaseJwtValidator));

        // ES256/JWKS JWT validator — replaces any prior IJwtValidator registration per ADR-AUTH-004
        services.Replace(ServiceDescriptor.Singleton<IJwtValidator, SupabaseJwtValidator>());

        // Supabase claims mapper — registered as both concrete type and interface
        services.AddSingleton<SupabaseClaimsMapper>();

        // Replace the default ClaimsMapper registered by AddMicroKitAuth() — per ADR-AUTH-004
        services.Replace(ServiceDescriptor.Singleton<IClaimsMapper, SupabaseClaimsMapper>());

        return services;
    }

    private static void ValidateOptions(SupabaseAuthOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ProjectUrl))
            throw new InvalidOperationException(
                "SupabaseAuthOptions.ProjectUrl must not be empty. " +
                "Set it via AddMicroKitAuthSupabase(o => o.ProjectUrl = \"https://xyz.supabase.co\").");

        if (!Uri.TryCreate(options.ProjectUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException(
                $"SupabaseAuthOptions.ProjectUrl '{options.ProjectUrl}' is not a valid absolute URI.");

        if (string.IsNullOrWhiteSpace(options.Audience))
            throw new InvalidOperationException(
                "SupabaseAuthOptions.Audience must not be empty. " +
                "Set it via AddMicroKitAuthSupabase(o => o.Audience = \"authenticated\").");

        if (string.IsNullOrWhiteSpace(options.Issuer))
            throw new InvalidOperationException(
                "SupabaseAuthOptions.Issuer must not be empty. " +
                "Set it via AddMicroKitAuthSupabase(o => o.Issuer = \"https://xyz.supabase.co/auth/v1\").");
    }
}
