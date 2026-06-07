namespace MicroKit.Auth.AspNetCore;

/// <summary>
/// Builder returned by <see cref="ServiceCollectionExtensions.AddMicroKitAuth"/> that
/// provides a fluent surface for provider and feature packages to register their services.
/// </summary>
/// <param name="services">The underlying service collection.</param>
public sealed class MicroKitAuthBuilder(IServiceCollection services)
{
    /// <summary>
    /// Gets the underlying service collection for extension methods to register additional services.
    /// </summary>
    public IServiceCollection Services { get; } = services;
}
