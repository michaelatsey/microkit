namespace MicroKit.Tenancy.AspNetCore;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods on <see cref="MultitenancyBuilder"/> for registering ASP.NET Core resolution strategies.
/// </summary>
public static class AspNetCoreMultitenancyBuilderExtensions
{
    /// <summary>
    /// Registers HTTP-based tenant resolution strategies.
    /// Call <see cref="MultitenancyApplicationBuilderExtensions.UseMultitenancy"/> on the request pipeline
    /// to activate per-request resolution.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Always-on strategies (registered by default):
    /// <list type="bullet">
    /// <item><see cref="HeaderTenantResolutionStrategy"/> — Order 10 (<c>X-Tenant-Id</c> header)</item>
    /// <item><see cref="RouteDataTenantResolutionStrategy"/> — Order 20 (<c>{tenantId}</c> route value)</item>
    /// <item><see cref="ClaimsTenantResolutionStrategy"/> — Order 40 (<c>tenant_id</c> claim)</item>
    /// </list>
    /// </para>
    /// <para>
    /// Opt-in strategies (set <see cref="AspNetCoreMultitenancyOptions.EnableSubdomain"/>
    /// or <see cref="AspNetCoreMultitenancyOptions.EnableHost"/> to <see langword="true"/>):
    /// <list type="bullet">
    /// <item><see cref="SubdomainTenantResolutionStrategy"/> — Order 30</item>
    /// <item><see cref="HostTenantResolutionStrategy"/> — Order 50</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <param name="builder">The multitenancy builder.</param>
    /// <param name="configure">Optional configuration for HTTP resolution strategies.</param>
    /// <returns>The same builder for chaining.</returns>
    public static MultitenancyBuilder AddAspNetCoreResolution(
        this MultitenancyBuilder builder,
        Action<AspNetCoreMultitenancyOptions>? configure = null)
    {
        var options = new AspNetCoreMultitenancyOptions();
        configure?.Invoke(options);

        builder.Services.AddSingleton(Options.Create(options));
        builder.Services.AddHttpContextAccessor();

        // Always-on strategies
        builder.Services.AddTransient<ITenantResolutionStrategy, HeaderTenantResolutionStrategy>();
        builder.Services.AddTransient<ITenantResolutionStrategy, RouteDataTenantResolutionStrategy>();
        builder.Services.AddTransient<ITenantResolutionStrategy, ClaimsTenantResolutionStrategy>();

        // Opt-in strategies — registered only when explicitly enabled via options
        if (options.EnableSubdomain)
            builder.Services.AddTransient<ITenantResolutionStrategy, SubdomainTenantResolutionStrategy>();

        if (options.EnableHost)
            builder.Services.AddTransient<ITenantResolutionStrategy, HostTenantResolutionStrategy>();

        return builder;
    }
}
