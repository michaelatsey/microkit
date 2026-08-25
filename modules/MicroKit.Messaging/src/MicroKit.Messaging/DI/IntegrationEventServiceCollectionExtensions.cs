namespace MicroKit.Messaging;

using MicroKit.Messaging.Publishing;
using MicroKit.Messaging.Serialization;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>Composition root for integration event publishing.</summary>
/// <remarks>
/// <para>
/// Two calls, on purpose. Declaring contracts is a <b>module</b> concern and runs once per module;
/// wiring the publisher is an <b>application</b> concern and runs once. Collapsing them is what
/// produced the defect this shape fixes — a per-application value registered from a per-module call
/// site, silently overwritten by whichever module ran last.
/// </para>
/// <para>
/// The staging writer is a third call, from the persistence package
/// (<c>AddEfCoreIntegrationEvents&lt;TContext&gt;()</c>), because this assembly has no EF Core
/// dependency and must not acquire one.
/// </para>
/// </remarks>
public static class IntegrationEventServiceCollectionExtensions
{
    /// <summary>Declares the integration events one module publishes. Call once per module.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="source">
    /// The emitting module, e.g. <c>/saasbtp/safety</c>. Identifies the module, not the deployment:
    /// it must survive extraction into a separate service unchanged, which is what makes that
    /// extraction invisible to consumers.
    /// </param>
    /// <param name="configure">The contracts this module publishes.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// Contributions accumulate — a plain <c>AddSingleton</c>, never <c>TryAdd</c> — and are
    /// composed into a single registry, so two modules declaring the same contract name is
    /// detected. It could not be if each module kept its own.
    /// </remarks>
    public static IServiceCollection AddIntegrationEventContracts(
        this IServiceCollection services,
        string source,
        Action<IntegrationEventContractBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new IntegrationEventContractBuilder(source);
        configure(builder);

        services.AddSingleton(builder.Build());
        return services;
    }

    /// <summary>
    /// Wires the contract registry and the publisher, and validates composition at startup.
    /// Call once.
    /// </summary>
    /// <param name="builder">The <see cref="MessagingBuilder"/> returned by
    /// <c>AddMicroKitMessaging()</c>.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <remarks>
    /// <para>
    /// The registry is composed here from every module's contribution and validated <b>eagerly</b>
    /// at startup, so a missing attribute or a duplicated contract name fails at boot rather than
    /// on the one code path that emits that event.
    /// </para>
    /// <para>
    /// <b>The publisher is scoped, and it matters.</b> It is resolved from the per-message
    /// execution scope; a singleton would capture one execution context and one writer, and stage
    /// every event against them — the classic captive dependency, which here would write rows with
    /// the wrong tenant into the wrong transaction rather than merely misbehave.
    /// </para>
    /// <para>
    /// This call does not supply <see cref="IIntegrationEventWriter"/>. Add a persistence adapter —
    /// <c>AddEfCoreIntegrationEvents&lt;TContext&gt;()</c> from
    /// <c>MicroKit.Messaging.EntityFrameworkCore</c> — or the publisher cannot be activated.
    /// </para>
    /// </remarks>
    public static MessagingBuilder AddIntegrationEventPublishing(this MessagingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton(
            sp => new IntegrationEventRegistry(sp.GetServices<IntegrationEventContracts>()));

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, IntegrationEventRegistryValidator>());

        // The publisher needs a serializer, and nothing else in this call chain supplies one:
        // IMessageSerializer is otherwise registered only by AddInProcessTransport() and
        // AddMediatRDomainEvents(). Without this line, publishing integration events without also
        // wiring the in-process transport fails when the publisher is first activated — inside a
        // handler, inside a transaction — rather than at composition. TryAdd, so a host or another
        // package that already supplied one keeps it; AddMediatRDomainEvents does the same for the
        // same reason.
        builder.Services.TryAddSingleton<IMessageSerializer, SystemTextJsonMessageSerializer>();

        builder.Services.TryAddScoped<IIntegrationEventPublisher, IntegrationEventPublisher>();

        return builder;
    }
}
