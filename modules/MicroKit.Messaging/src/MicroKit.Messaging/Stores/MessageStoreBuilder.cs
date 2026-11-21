using System.Reflection;

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
}
