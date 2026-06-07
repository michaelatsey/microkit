namespace MicroKit.Auth.AspNetCore;

/// <summary>
/// An <see cref="IAuthorizationPolicyProvider"/> that recognises policy names with the prefix
/// <c>MicroKit.Auth.Permission:{resource}:{action}</c> and dynamically builds an
/// <see cref="AuthorizationPolicy"/> containing a <see cref="PermissionAuthorizationRequirement"/>.
/// All other policy names are delegated to the <see cref="DefaultAuthorizationPolicyProvider"/> fallback.
/// </summary>
/// <remarks>
/// Registered by <see cref="ServiceCollectionExtensions.AddMicroKitAuth"/> as a Singleton,
/// overriding the default <see cref="DefaultAuthorizationPolicyProvider"/>.
/// </remarks>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : IAuthorizationPolicyProvider
{
    /// <summary>
    /// Prefix that distinguishes permission-based policies from other named policies.
    /// </summary>
    public const string PolicyPrefix = "MicroKit.Auth.Permission:";

    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    /// <inheritdoc />
    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(PolicyPrefix, StringComparison.Ordinal))
            return _fallback.GetPolicyAsync(policyName);

        var remainder = policyName[PolicyPrefix.Length..];
        var colonIndex = remainder.IndexOf(':');
        if (colonIndex <= 0 || colonIndex == remainder.Length - 1)
            return _fallback.GetPolicyAsync(policyName);

        var resource = remainder[..colonIndex];
        var action = remainder[(colonIndex + 1)..];

        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionAuthorizationRequirement(Permission.Of(resource, action)))
            .Build();

        return Task.FromResult<AuthorizationPolicy?>(policy);
    }

    /// <inheritdoc />
    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() =>
        _fallback.GetDefaultPolicyAsync();

    /// <inheritdoc />
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() =>
        _fallback.GetFallbackPolicyAsync();
}
