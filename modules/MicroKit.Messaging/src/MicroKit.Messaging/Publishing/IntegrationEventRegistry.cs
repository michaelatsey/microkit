namespace MicroKit.Messaging.Publishing;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// The integration contract surface of the application, in both directions: every event that may be
/// emitted, with its wire name and its emitting module; and every wire name this process
/// understands, with the local CLR type it deserializes into.
/// </summary>
/// <remarks>
/// <para>
/// Having an actual list buys two cheap tests: every <see cref="IIntegrationEvent"/> in a module is
/// registered — a forgotten one fails the build instead of failing at runtime on the single path
/// that emits it — and <see cref="ContractNames"/> is snapshot-tested, so an accidental rename
/// fails, which is the correct behaviour for a public contract. <see cref="SubscribedContractNames"/>
/// does the same for the consuming side, which nothing else would catch in a service that only
/// consumes.
/// </para>
/// <para>
/// <b>The reverse direction resolves a name to <i>this process's own</i> type</b>, never to the
/// producer's: the producer's CLR type is useless to a consumer, which is exactly why a stable wire
/// name exists. What is registered is the local end of a binding whose other end is a string.
/// Without it a consumer holding a payload and a name has nothing to deserialize into, because it
/// does not hold the producer's assembly and <c>Type.GetType(assemblyQualifiedName)</c> therefore
/// cannot resolve. That works in process only by accident, and it is the property a transport
/// depends on.
/// </para>
/// <para>
/// <b>One local type per contract name, per process.</b> The reverse direction is a function, and
/// two types claiming one name is rejected at composition. It has to be: a relay deserializes once,
/// before any fan-out, so an ambiguous name would be resolved by picking arbitrarily — silently
/// producing the wrong type from structurally-compatible JSON. Modules sharing a contract share the
/// CLR type. Per-consumer mirror types are consequently not expressible, which also matches what
/// the in-process path already does: it writes one inbox row per consumer, all carrying one event
/// type.
/// </para>
/// <para>
/// <b>Publishing a contract also binds its name.</b> A module that declares
/// <c>Publishes&lt;T&gt;()</c> makes that name resolvable to <c>T</c> without a second declaration,
/// so a modular monolith routes its own contracts for free. <c>Consumes&lt;T&gt;()</c> exists for a
/// contract a module does not publish itself, and declaring both is a no-op rather than a conflict —
/// a module must not have to know whether its dependency happens to be in-process.
/// </para>
/// <para>
/// Singleton, composed once from every module's contribution and read-only afterwards.
/// </para>
/// </remarks>
public sealed class IntegrationEventRegistry
{
    private readonly Dictionary<Type, IntegrationEventRegistration> _byType;
    private readonly Dictionary<string, Type> _byName;
    private readonly HashSet<string> _subscribedNames;

    /// <summary>
    /// Composes every module's published contracts and declared subscriptions into one registry,
    /// rejecting collisions in both directions.
    /// </summary>
    /// <param name="contributions">Every module's published contracts.</param>
    /// <param name="subscriptions">Every module's declared subscriptions.</param>
    /// <exception cref="IntegrationEventConfigurationException">
    /// Two types claim one contract name — whether both publish it, both consume it, or one of
    /// each — or one type is published twice.
    /// </exception>
    /// <remarks>
    /// Composing all modules into a single registry is what makes a cross-module name collision
    /// detectable at all. With one registry per module — the shape a per-module options object
    /// would have produced — two modules could publish different payloads under one name and
    /// nothing would notice until a consumer failed to deserialize. Taking both kinds of
    /// contribution here extends that to the collision that spans them: a type publishing a name a
    /// different type claims to consume is visible only to something holding both.
    /// </remarks>
    public IntegrationEventRegistry(
        IEnumerable<IntegrationEventContracts> contributions,
        IEnumerable<IntegrationEventSubscriptions> subscriptions)
    {
        ArgumentNullException.ThrowIfNull(contributions);
        ArgumentNullException.ThrowIfNull(subscriptions);

        _byType = [];
        _byName = new Dictionary<string, Type>(StringComparer.Ordinal);
        _subscribedNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var registration in contributions.SelectMany(c => c.Registrations))
        {
            if (_byType.TryGetValue(registration.EventType, out var existing))
            {
                throw new IntegrationEventConfigurationException(
                    $"'{registration.EventType.FullName}' is declared twice: as " +
                    $"'{existing.ContractName}' from '{existing.Source}' and as " +
                    $"'{registration.ContractName}' from '{registration.Source}'. One type has one " +
                    "contract.");
            }

            if (_byName.TryGetValue(registration.ContractName, out var owner))
            {
                // Both sources are nameable here, and both are named: an operator meeting this at
                // boot has a type name and nothing else otherwise, and in a monolith the module is
                // the half that says where to look. _byType[owner] is safe — inside this loop the
                // name index holds published types exclusively.
                throw new IntegrationEventConfigurationException(
                    $"Contract name '{registration.ContractName}' is declared by both " +
                    $"'{owner.FullName}' from '{_byType[owner].Source}' and " +
                    $"'{registration.EventType.FullName}' from '{registration.Source}'. Two types " +
                    "publishing under one name are indistinguishable on the wire, and a consumer " +
                    "would deserialize whichever it happened to receive into the wrong shape.");
            }

            _byType[registration.EventType] = registration;
            _byName[registration.ContractName] = registration.EventType;
        }

        // Subscriptions fold into the name index ONLY. A consumed contract is not a published one:
        // it must never reach _byType, or this application would offer to publish an event it
        // merely understands — and with no source to publish it from.
        foreach (var subscription in subscriptions.SelectMany(s => s.Subscriptions))
        {
            if (_byName.TryGetValue(subscription.ContractName, out var bound)
                && bound != subscription.EventType)
            {
                // The incumbent gets its module named when it has one — it does when it publishes,
                // and it does not when it merely subscribes. That is not an omission to repair: a
                // subscription carries no source precisely so it cannot invent one.
                var incumbent = _byType.TryGetValue(bound, out var boundRegistration)
                    ? $"'{bound.FullName}' from '{boundRegistration.Source}'"
                    : $"'{bound.FullName}'";

                throw new IntegrationEventConfigurationException(
                    $"Contract name '{subscription.ContractName}' is claimed by both " +
                    $"{incumbent} and '{subscription.EventType.FullName}'. A contract name " +
                    "resolves to exactly one local type: a message is deserialized once, before " +
                    "any fan-out, so a second claimant could only be honoured by picking one " +
                    "arbitrarily and silently producing the wrong shape. Modules sharing a " +
                    "contract share the type.");
            }

            // Same name, same type — a module declaring a contract it also publishes, or two
            // modules consuming one contract. Both are nominal, so this is a no-op rather than a
            // conflict: a module must not have to know whether its producer is in-process.
            _byName[subscription.ContractName] = subscription.EventType;
            _subscribedNames.Add(subscription.ContractName);
        }
    }

    /// <summary>
    /// Gets every contract name this application <b>publishes</b>, ordered. The snapshot test
    /// asserts on this. Declared subscriptions are deliberately absent — consuming a contract is
    /// not publishing it.
    /// </summary>
    public IReadOnlyList<string> ContractNames =>
        [.. _byType.Values.Select(r => r.ContractName).Order(StringComparer.Ordinal)];

    /// <summary>
    /// Gets every contract name this application has explicitly declared it <b>understands</b>,
    /// ordered. Contracts made resolvable by publishing them are not listed here.
    /// </summary>
    /// <remarks>
    /// Snapshot-testable for the same reason <see cref="ContractNames"/> is, and it matters more:
    /// in a service that only consumes, a mistyped or renamed contract name on a local type binds
    /// nothing, and no publish-side snapshot would ever notice.
    /// </remarks>
    public IReadOnlyList<string> SubscribedContractNames =>
        [.. _subscribedNames.Order(StringComparer.Ordinal)];

    /// <summary>Gets the number of published contracts.</summary>
    public int Count => _byType.Count;

    /// <summary>Resolves the contract an event type is published under.</summary>
    /// <param name="eventType">The runtime type of the event being published.</param>
    /// <returns>The registration for <paramref name="eventType"/>.</returns>
    /// <exception cref="IntegrationEventConfigurationException">The type is not registered.</exception>
    /// <remarks>
    /// <b>There is deliberately no <c>TryResolveContract</c> counterpart</b>, and the asymmetry with
    /// <see cref="TryResolveLocalType"/> is the point rather than an omission. A miss in this
    /// direction is a programming error — code holding an instance it never declared — and the only
    /// correct response is to throw. A miss in the reverse direction is <i>data</i>: a name off the
    /// wire that this process was never taught, which a drain path must classify rather than crash
    /// on. Only the direction fed by untrusted input needs a non-throwing overload.
    /// </remarks>
    public IntegrationEventRegistration ResolveContract(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        return _byType.TryGetValue(eventType, out var registration)
            ? registration
            : throw new IntegrationEventConfigurationException(
                $"'{eventType.FullName}' is not a registered integration event. Add " +
                $"events.Publishes<{eventType.Name}>() to the module's composition root. " +
                "Registration is explicit so a module's published contracts stay reviewable.");
    }

    /// <summary>
    /// Attempts to resolve the local CLR type a contract name deserializes into.
    /// </summary>
    /// <param name="contractName">The wire contract name, as received.</param>
    /// <param name="eventType">The local type bound to that name, when one is bound.</param>
    /// <returns>
    /// <see langword="true"/> when the name is bound in this process; otherwise
    /// <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// Matching is <b>ordinal</b>. A contract name is a wire identity, not display text: a
    /// case-insensitive or culture-sensitive match would collapse two distinct contracts onto one
    /// local type and deserialize the wrong shape without raising anything.
    /// </remarks>
    public bool TryResolveLocalType(string contractName, [NotNullWhen(true)] out Type? eventType)
    {
        ArgumentNullException.ThrowIfNull(contractName);

        return _byName.TryGetValue(contractName, out eventType);
    }

    /// <summary>
    /// Resolves the local CLR type a contract name deserializes into.
    /// </summary>
    /// <param name="contractName">The wire contract name, as received.</param>
    /// <returns>The local type bound to <paramref name="contractName"/>.</returns>
    /// <exception cref="IntegrationEventConfigurationException">
    /// No local type is bound to that name.
    /// </exception>
    /// <remarks>
    /// An unbound name is a <b>permanent</b> condition, not a transient one: it cannot become bound
    /// without a redeploy. A caller draining a queue should classify it as such — dead-letter on
    /// first sight rather than spend the whole retry budget re-reaching a verdict already reached on
    /// the first attempt. Use <see cref="TryResolveLocalType"/> where the miss is expected and the
    /// caller supplies its own classification.
    /// </remarks>
    public Type ResolveLocalType(string contractName)
    {
        ArgumentNullException.ThrowIfNull(contractName);

        return _byName.TryGetValue(contractName, out var eventType)
            ? eventType
            : throw new IntegrationEventConfigurationException(
                $"Contract name '{contractName}' resolves to no local type. Add " +
                "events.Consumes<TEvent>() to the module's composition root, with " +
                $"[IntegrationEvent(\"{contractName}\")] on that type. The assembly-qualified name " +
                "of the producer's type cannot serve here: this process does not hold that " +
                "assembly, which is why the contract name exists.");
    }
}
