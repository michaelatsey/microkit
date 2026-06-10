namespace MicroKit.Auth.Supabase;

/// <summary>
/// Configuration options for Supabase JWT authentication.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="JwksUri"/> is auto-derived from <see cref="ProjectUrl"/> using the canonical
/// Supabase JWKS endpoint pattern: <c>{ProjectUrl}/auth/v1/.well-known/jwks.json</c>.
/// </para>
/// <para>
/// Validated eagerly at startup via
/// <see cref="ServiceCollectionExtensions.AddMicroKitAuthSupabase"/>. An
/// <see cref="InvalidOperationException"/> is thrown immediately if required fields are missing
/// or malformed. The application will not start with a misconfigured Supabase integration.
/// </para>
/// </remarks>
public sealed record SupabaseAuthOptions
{
    /// <summary>
    /// The Supabase project URL, e.g. <c>https://xyz.supabase.co</c>.
    /// Must not be empty. Used to derive <see cref="JwksUri"/> and to validate <c>iss</c> claims.
    /// </summary>
    public required string ProjectUrl { get; init; }

    /// <summary>
    /// The JWKS endpoint URI, auto-derived from <see cref="ProjectUrl"/>.
    /// </summary>
    /// <remarks>
    /// Supabase canonical pattern: <c>{ProjectUrl}/auth/v1/.well-known/jwks.json</c>.
    /// Override is not supported in Phase 1; self-hosted Supabase consumers may set
    /// <see cref="ProjectUrl"/> to the base URL of their self-hosted instance.
    /// </remarks>
    public Uri JwksUri => new($"{ProjectUrl.TrimEnd('/')}/auth/v1/.well-known/jwks.json");

    /// <summary>
    /// The expected JWT audience. Supabase default: <c>authenticated</c>.
    /// </summary>
    public string Audience { get; init; } = "authenticated";

    /// <summary>
    /// The expected JWT issuer. Supabase pattern: <c>{ProjectUrl}/auth/v1</c>.
    /// Must not be empty — set this to match the <c>iss</c> claim in your Supabase JWTs.
    /// </summary>
    public string Issuer { get; init; } = string.Empty;

    /// <summary>
    /// How long to cache the JWKS signing keys before re-fetching. Default: 60 minutes.
    /// </summary>
    public TimeSpan JwksCacheDuration { get; init; } = TimeSpan.FromMinutes(60);

    /// <summary>
    /// Minimum interval between forced JWKS key-rotation refreshes. Default: 5 minutes.
    /// Prevents hammering the JWKS endpoint when multiple concurrent requests encounter
    /// an unknown key simultaneously.
    /// </summary>
    public TimeSpan KeyRotationCooldown { get; init; } = TimeSpan.FromMinutes(5);
}
