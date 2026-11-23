using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroKit.Messaging.Spi;

/// <summary>
/// The registration of one message store, handed by
/// <see cref="MessageStoreRegistrationExtensions.AddMessageStore"/> to a store provider.
/// </summary>
/// <remarks>
/// <para>
/// Part of the <b>store SPI</b>. A provider registers its store's services keyed by
/// <see cref="Key"/> — <see cref="IOutboxWriter"/>, <see cref="IOutboxProcessorStore"/>,
/// <see cref="IOutboxRetentionStore"/>, <see cref="IOutboxAdminStore"/> and the inbox contracts —
/// and the engine resolves each of them per scope by that key. A store is the outbox and the inbox
/// of one unit of persistence.
/// </para>
/// <para>
/// A second registration of one key reaches the same store: its bindings and checks add to the
/// first one's, so a provider registers its services with <c>TryAdd</c>.
/// </para>
/// <para>
/// <b>Single or partitioned (#191).</b> A store is single by default: it is its one instance, drained
/// as configured. <see cref="PartitionedBy{TSource, TInitializer}"/> makes it partitioned: the engine
/// drains the instances its source supplies at runtime — one database per tenant, one per shard —
/// each in scopes the initializer prepares for it. The store itself, its key and its keyed services
/// stay what the host configured.
/// </para>
/// </remarks>
public sealed class MessageStoreBuilder
{
    private readonly MessagingComposition.StoreRegistration _registration;

    internal MessageStoreBuilder(
        IServiceCollection services, MessagingComposition.StoreRegistration registration)
    {
        Services = services;
        _registration = registration;
    }

    /// <summary>Gets the store's key.</summary>
    public MessageStoreKey Key => _registration.Key;

    /// <summary>Gets the service collection the store's services are registered into.</summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Binds <paramref name="assembly"/> to this store: the handlers and the message types declared
    /// in it use this store. Needed only when several stores are configured.
    /// </summary>
    /// <param name="assembly">The assembly to bind.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>Binding one assembly twice to one store is a no-op; to two stores, a start failure.</remarks>
    public MessageStoreBuilder BindAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        if (!_registration.Assemblies.Contains(assembly))
        {
            _registration.Assemblies.Add(assembly);
        }

        return this;
    }

    /// <summary>
    /// Adds a check run when the host starts, with the composition validation: each string it
    /// returns is a problem that fails the start, and should name its fix.
    /// </summary>
    /// <param name="check">The check. It receives the root service provider.</param>
    /// <returns>The same builder, for chaining.</returns>
    public MessageStoreBuilder AddStartupCheck(Func<IServiceProvider, IEnumerable<string>> check)
    {
        ArgumentNullException.ThrowIfNull(check);
        _registration.StartupChecks.Add(check);
        return this;
    }

    /// <summary>
    /// Adds a check a partitioned store runs once, in the prepared scope of its first instance to start,
    /// instead of at host start: a check that builds the store's <c>DbContext</c>, which only an
    /// instance's scope can build. A single store never runs it — its start checks cover it.
    /// </summary>
    /// <param name="check">
    /// The check. It receives the instance scope's provider, prepared by the store's
    /// <see cref="IMessageStoreInstanceInitializer"/>. Each string it returns is a problem of the store:
    /// logged at Critical, and no instance of it is drained until a restart. An exception is the
    /// instance's: that instance backs off, and the check runs again at the next attempt.
    /// </param>
    /// <returns>The same builder, for chaining.</returns>
    public MessageStoreBuilder AddInstanceCheck(Func<IServiceProvider, IEnumerable<string>> check)
    {
        ArgumentNullException.ThrowIfNull(check);
        _registration.InstanceChecks.Add(check);
        return this;
    }

    /// <summary>
    /// Makes this store partitioned (#191): the engine drains the instances <typeparamref name="TSource"/>
    /// supplies, each in scopes <typeparamref name="TInitializer"/> prepares for it.
    /// </summary>
    /// <typeparam name="TSource">
    /// The instance source: a singleton of its own type — the instance a host adds instances to, and
    /// that an initializer may take — registered unless the host registered one first, and resolved by
    /// the engine through a registration keyed by <see cref="Key"/>. Give each partitioned store a source
    /// type of its own.
    /// </typeparam>
    /// <typeparam name="TInitializer">The instance-scope initializer, registered scoped, keyed by <see cref="Key"/>.</typeparam>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// One source per store: a second call fails the host start. The store's key, its keyed services,
    /// its handler bindings and the resolution of its publications are unchanged; what changes is what
    /// the engine drains. Each instance has its own loops, its own back-off, and its own place in the
    /// gate bounded by <see cref="Options.StoreInstanceOptions.MaxConcurrentBatches"/>.
    /// </para>
    /// <para>
    /// No table is created and no migration runs: a source declares an instance once its database is
    /// provisioned.
    /// </para>
    /// </remarks>
    public MessageStoreBuilder PartitionedBy<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TSource,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TInitializer>()
        where TSource : class, IMessageStoreInstanceSource
        where TInitializer : class, IMessageStoreInstanceInitializer
    {
        _registration.PartitionCalls++;
        Services.TryAddSingleton<TSource>();
        Services.TryAddKeyedSingleton<IMessageStoreInstanceSource>(Key, static (sp, _) => sp.GetRequiredService<TSource>());
        Services.TryAddKeyedScoped<IMessageStoreInstanceInitializer, TInitializer>(Key);
        return this;
    }
}
