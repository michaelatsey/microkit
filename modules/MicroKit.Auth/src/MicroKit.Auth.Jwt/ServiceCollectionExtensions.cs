using System.Text;

namespace MicroKit.Auth.Jwt;

/// <summary>
/// DI registration extensions for <c>MicroKit.Auth.Jwt</c>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers HMAC-SHA256 JWT validation and generation services.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="JwtOptions"/> is validated eagerly — an <see cref="InvalidOperationException"/>
    /// is thrown immediately at startup if <see cref="JwtOptions.Issuer"/>,
    /// <see cref="JwtOptions.Audience"/>, or <see cref="JwtOptions.Secret"/> are invalid,
    /// or if the secret is shorter than 32 UTF-8 bytes. The application will not start with
    /// a misconfigured JWT secret.
    /// </para>
    /// <para>
    /// <see cref="IJwtTokenGenerator"/> depends on <see cref="IClaimsMapper"/>. Call
    /// <c>AddMicroKitAuthCore()</c> before this method to ensure the default
    /// <see cref="IClaimsMapper"/> is registered, or register a custom implementation first.
    /// </para>
    /// <para>Phase 1 scope: HS256 (HMAC-SHA256) only. See ADR-AUTH-007.</para>
    /// </remarks>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configure">Action that populates the <see cref="JwtOptions"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown immediately when <see cref="JwtOptions.Issuer"/>, <see cref="JwtOptions.Audience"/>,
    /// or <see cref="JwtOptions.Secret"/> are empty, or when <see cref="JwtOptions.Secret"/> is
    /// shorter than 32 UTF-8 bytes.
    /// </exception>
    public static IServiceCollection AddMicroKitAuthJwt(
        this IServiceCollection services,
        Action<JwtOptions> configure)
    {
        var options = new JwtOptions();
        configure(options);

        ValidateOptions(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton<IJwtValidator, JwtValidator>();
        services.TryAddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

        return services;
    }

    private static void ValidateOptions(JwtOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Issuer))
            throw new InvalidOperationException(
                "JwtOptions.Issuer must not be empty. " +
                "Set it via AddMicroKitAuthJwt(o => o.Issuer = \"...\").");

        if (string.IsNullOrWhiteSpace(options.Audience))
            throw new InvalidOperationException(
                "JwtOptions.Audience must not be empty. " +
                "Set it via AddMicroKitAuthJwt(o => o.Audience = \"...\").");

        if (string.IsNullOrWhiteSpace(options.Secret))
            throw new InvalidOperationException(
                "JwtOptions.Secret must not be empty. " +
                "Provide a strong HMAC secret via AddMicroKitAuthJwt(o => o.Secret = \"...\").");

        if (Encoding.UTF8.GetByteCount(options.Secret) < 32)
            throw new InvalidOperationException(
                "JwtOptions.Secret must be at least 32 UTF-8 bytes. " +
                "Provide a longer secret via AddMicroKitAuthJwt(o => o.Secret = \"...\").");
    }
}
