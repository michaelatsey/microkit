namespace MicroKit.Messaging;

using MicroKit.Messaging.Processing;
using MicroKit.Messaging.Publishing;
using MicroKit.Messaging.Serialization;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>Composition root for integration event publishing and consumption.</summary>
/// <remarks>
/// <para>
/// Two levels, on purpose. Declaring contracts — published or consumed — is a <b>module</b> concern
/// and runs once per module; wiring the registry, the validator and the publisher is an
/// <b>application</b> concern and runs once. Collapsing them is what produced the defect this shape
/// fixes — a per-application value registered from a per-module call site, silently overwritten by
/// whichever module ran last.
/// </para>
/// <para>
/// At the application level there are two entry points rather than one, because a service may
/// publish, consume, or both: <see cref="AddIntegrationEventPublishing"/> and
/// <see cref="AddIntegrationEventConsumption"/>. They share the registry and the validator and are
/// safe to call together, in either order.
/// </para>
/// <para>
/// The staging writer is a further call, from the persistence package
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
    /// Declares the integration events one module understands — those it does not publish itself.
    /// Call once per module.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">The contracts this module consumes.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <b>No <c>source</c>, deliberately.</b> A source names the module that <i>emitted</i> an
    /// event and is written onto the staged row as the emitter's identity. A consumer emitted
    /// nothing, so any value it supplied would be false data in the one column that must survive a
    /// module's extraction into its own service. Reusing
    /// <see cref="AddIntegrationEventContracts"/> with a nominal source would put exactly that
    /// there; making the parameter optional would trade a compile-time guarantee on the publish
    /// path for a runtime guard.
    /// </para>
    /// <para>
    /// A module needs this only for a contract it does not publish. Publishing already binds the
    /// name to the declaring type, and declaring both is accepted as a no-op — a module must not
    /// have to know whether its producer happens to be in-process.
    /// </para>
    /// <para>
    /// Contributions accumulate, for the same reason published contracts do: one composed registry
    /// is what makes a collision across two modules visible.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddIntegrationEventSubscriptions(
        this IServiceCollection services,
        Action<IntegrationEventSubscriptionBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new IntegrationEventSubscriptionBuilder();
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
    /// <para>
    /// A service that also consumes calls <see cref="AddIntegrationEventConsumption"/> as well;
    /// the two share the registry, the validator and the envelope receiver, and may be called in
    /// either order.
    /// </para>
    /// </remarks>
    public static MessagingBuilder AddIntegrationEventPublishing(this MessagingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        AddContractRegistry(builder.Services);

        // No IMessageSerializer TryAdd here. AddMicroKitMessaging() supplies the default, and this
        // method is an extension on the builder that method returns — so it cannot run without it
        // (ADR-MSG-019). A fourth TryAdd of the same service would be unreachable code that reads
        // like a safeguard.

        builder.Services.TryAddScoped<IIntegrationEventPublisher, IntegrationEventPublisher>();

        return builder;
    }

    /// <summary>
    /// Wires the contract registry and validates composition at startup, for a service that
    /// consumes integration events. Call once.
    /// </summary>
    /// <param name="builder">The <see cref="MessagingBuilder"/> returned by
    /// <c>AddMicroKitMessaging()</c>.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <remarks>
    /// <para>
    /// The registry, its startup validator and the <see cref="IEnvelopeReceiver"/> a transport
    /// provider hands arriving envelopes to — and <b>nothing else</b>: no publisher, no serializer
    /// default. A service that only consumes publishes nothing and must not be given a publisher it
    /// cannot legitimately use.
    /// </para>
    /// <para>
    /// <b>Why this exists at all.</b> Without it, a consumer-only service would have no
    /// application-level call, so its registry would be composed lazily on first resolve — which on
    /// a drain path is inside a handler, inside a transaction, where a duplicated contract name is
    /// misclassified as a transient failure and retried forever against something no retry can fix.
    /// That is precisely the failure mode the startup validator exists to prevent, and a consumer
    /// has as much need of it as a producer.
    /// </para>
    /// <para>
    /// <b>Safe alongside <see cref="AddIntegrationEventPublishing"/>, in either order.</b> A
    /// service that both publishes and consumes calls both and gets one registry, one validator and
    /// one receiver: the registry and the receiver are registered with <c>TryAddSingleton</c>, which
    /// dedups on service type, and the validator with <c>TryAddEnumerable</c>, which dedups on
    /// implementation type.
    /// </para>
    /// </remarks>
    public static MessagingBuilder AddIntegrationEventConsumption(this MessagingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        AddContractRegistry(builder.Services);

        return builder;
    }

    /// <summary>
    /// The three registrations both application-level entry points need, in one place so they
    /// cannot drift apart.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every registration must stay <c>TryAdd</c>. Under a plain <c>AddSingleton</c> the second
    /// caller would append a second registry descriptor, Microsoft DI would resolve the last one,
    /// and a service that both publishes and consumes would get a different registry depending on
    /// the order the two calls happened to be written in — with the boot validation running against
    /// one of them.
    /// </para>
    /// <para>
    /// <b><see cref="IEnvelopeReceiver"/> belongs here and nowhere else.</b> It is the one place
    /// guaranteed to have composed the registry the receiver resolves a contract name through, and
    /// it is reached by both entry points — so a service that only consumes and a service that also
    /// publishes both end up able to receive, in either call order. Registering it in
    /// <c>AddMicroKitMessaging()</c> instead would put a receiver in a host that declares no
    /// contracts at all, where it could not be constructed.
    /// </para>
    /// <para>
    /// It gets no builder method of its own. There is nothing to configure, and a method whose
    /// omission is invisible until a provider fails to resolve the seam is worse than no method:
    /// a broker package's <c>Add{Provider}Transport()</c> must be able to assume the receiver is
    /// already there.
    /// </para>
    /// </remarks>
    private static void AddContractRegistry(IServiceCollection services)
    {
        services.TryAddSingleton(sp => new IntegrationEventRegistry(
            sp.GetServices<IntegrationEventContracts>(),
            sp.GetServices<IntegrationEventSubscriptions>()));

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, IntegrationEventRegistryValidator>());

        // Singleton: it creates its own execution scope per envelope, so a provider's consume loop
        // may hold one for its lifetime and cannot share a DbContext across messages by accident.
        services.TryAddSingleton<IEnvelopeReceiver, EnvelopeReceiver>();
    }
}
