using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.Logging;

/// <summary>
/// Fluent builder for registering enrichers on the MicroKit logging pipeline.
/// Obtained from the <c>configureBuilder</c> parameter of
/// <see cref="LoggingBuilderExtensions.AddMicroKitLogging"/>.
/// </summary>
public sealed class MicroKitLoggingBuilder
{
    private readonly IServiceCollection _services;

    internal MicroKitLoggingBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>
    /// Registers a synchronous enricher by type. The instance is resolved from DI as a singleton.
    /// </summary>
    /// <typeparam name="TEnricher">The enricher type to register. Must implement <see cref="ILogEnricher"/>.</typeparam>
    /// <returns>The current builder for chaining.</returns>
    public MicroKitLoggingBuilder AddEnricher<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TEnricher>() where TEnricher : class, ILogEnricher
    {
        _services.AddSingleton<ILogEnricher, TEnricher>();
        return this;
    }

    /// <summary>
    /// Registers a synchronous enricher instance as a singleton.
    /// </summary>
    /// <param name="enricher">The enricher instance to register. Must not be null.</param>
    /// <returns>The current builder for chaining.</returns>
    public MicroKitLoggingBuilder AddEnricher(ILogEnricher enricher)
    {
        ArgumentNullException.ThrowIfNull(enricher);
        _services.AddSingleton<ILogEnricher>(enricher);
        return this;
    }

    /// <summary>
    /// Registers an asynchronous enricher by type. The instance is resolved from DI as a singleton.
    /// </summary>
    /// <typeparam name="TEnricher">The enricher type to register. Must implement <see cref="IAsyncLogEnricher"/>.</typeparam>
    /// <returns>The current builder for chaining.</returns>
    public MicroKitLoggingBuilder AddAsyncEnricher<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TEnricher>() where TEnricher : class, IAsyncLogEnricher
    {
        _services.AddSingleton<IAsyncLogEnricher, TEnricher>();
        return this;
    }

    /// <summary>
    /// Registers an asynchronous enricher instance as a singleton.
    /// </summary>
    /// <param name="enricher">The enricher instance to register. Must not be null.</param>
    /// <returns>The current builder for chaining.</returns>
    public MicroKitLoggingBuilder AddAsyncEnricher(IAsyncLogEnricher enricher)
    {
        ArgumentNullException.ThrowIfNull(enricher);
        _services.AddSingleton<IAsyncLogEnricher>(enricher);
        return this;
    }
}
