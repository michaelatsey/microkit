using MicroKit.Messaging.Stores;

namespace MicroKit.Messaging.Processing;

/// <summary>
/// What the engine holds for one instance of a partitioned store while it drains it (#191): its own
/// back-off state, outbox and inbox, the signal that stops its loops, the wake of its outbox loop
/// (#215), and how a scope is prepared for it.
/// </summary>
/// <remarks>
/// <para>
/// Created by the store's reconciler when the instance appears, released once its four loops have
/// ended — never by a delivery, which reaches it through
/// <see cref="StoreInstanceRegistry.Find(MessageStoreKey, MessageContext)"/>.
/// A delivery still holding it after the release writes into objects nothing reads.
/// </para>
/// <para>
/// <b>Stopping is not cancelling.</b> <see cref="Stopping"/> fires on removal or host stop and is read by
/// the loops between batches and while they wait; a batch in flight runs on the host's token alone, so a
/// removal lets it settle.
/// </para>
/// </remarks>
internal sealed class StoreInstanceState : IDisposable
{
    private readonly CancellationTokenSource _stopping;
    private volatile bool _removed;
    private volatile bool _wanted;
    private volatile bool _released;

    /// <summary>Initializes a new <see cref="StoreInstanceState"/>.</summary>
    /// <param name="store">The store.</param>
    /// <param name="instance">The instance.</param>
    /// <param name="outboxBackoff">Its outbox's endpoint back-off.</param>
    /// <param name="inboxTenants">Its inbox's tenant back-off.</param>
    /// <param name="gate">The gate its outbox and inbox batches pass.</param>
    /// <param name="registry">Runs its store's check.</param>
    /// <param name="tagInstances">Whether its instruments carry <c>store.instance</c>.</param>
    /// <param name="stoppingToken">The host's token.</param>
    public StoreInstanceState(
        MessageStoreKey store,
        MessageStoreInstance instance,
        OutboxEndpointBackoff outboxBackoff,
        TenantBackoff inboxTenants,
        FifoGate gate,
        StoreInstanceRegistry registry,
        bool tagInstances,
        CancellationToken stoppingToken)
    {
        Store = store;
        Instance = instance;
        Key = new StoreInstanceKey(store, instance.Id);
        OutboxBackoff = outboxBackoff;
        InboxTenants = inboxTenants;
        Gate = gate;
        Registry = registry;
        InstanceTag = tagInstances ? instance.Id : null;
        LogScope = [new("Store", store.Value), new("Instance", instance.Id)];
        _stopping = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
    }

    /// <summary>Gets the store.</summary>
    public MessageStoreKey Store { get; }

    /// <summary>Gets the instance.</summary>
    public MessageStoreInstance Instance { get; }

    /// <summary>Gets the key its state and series are registered under.</summary>
    public StoreInstanceKey Key { get; }

    /// <summary>Gets its outbox's endpoint back-off.</summary>
    public OutboxEndpointBackoff OutboxBackoff { get; }

    /// <summary>Gets its inbox's tenant back-off.</summary>
    public TenantBackoff InboxTenants { get; }

    /// <summary>Gets the wake of its outbox loop: a commit in a scope prepared for it signals it (#215).</summary>
    public LoopWake OutboxWake { get; } = new();

    /// <summary>Gets the gate its outbox and inbox batches pass.</summary>
    public FifoGate Gate { get; }

    /// <summary>Gets the registry that runs its store's check.</summary>
    public StoreInstanceRegistry Registry { get; }

    /// <summary>Gets the <c>store.instance</c> tag of its instruments; null while instances are not tagged.</summary>
    public string? InstanceTag { get; }

    /// <summary>Gets the logging scope of its loops' iterations: <c>Store</c> and <c>Instance</c>.</summary>
    public KeyValuePair<string, object?>[] LogScope { get; }

    /// <summary>Gets a token that fires on its removal or the host's stop: its loops stop claiming.</summary>
    public CancellationToken Stopping => _stopping.Token;

    /// <summary>
    /// Gets a value indicating whether its source removed it and has not listed it again: no new
    /// delivery maps to it.
    /// </summary>
    public bool IsRemoved => _removed && !_wanted;

    /// <summary>
    /// Gets or sets a value indicating whether its source listed it again while its loops still settle:
    /// its deliveries keep mapping to it until it is released. Between its release and its successor's
    /// start — the next round, signalled by the release — a delivery maps to no instance (1410) and is
    /// redelivered.
    /// </summary>
    public bool IsWanted
    {
        get => _wanted;
        set => _wanted = value;
    }

    /// <summary>Gets or sets a value indicating whether its state was released: its successor may start.</summary>
    public bool IsReleased
    {
        get => _released;
        set => _released = value;
    }

    /// <summary>Gets or sets the task that completes once its loops have ended and its state is released.</summary>
    public Task Drained { get; set; } = Task.CompletedTask;

    /// <summary>Stops its loops after their batches in flight settle.</summary>
    public void Remove()
    {
        _removed = true;
        try
        {
            _stopping.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Its loops had already ended — its store refused — and its state was released.
        }
    }

    /// <summary>
    /// Prepares a scope for this instance: names the store and the instance on the scope, then runs the
    /// store's instance initializer, before any other service of the scope is resolved.
    /// </summary>
    /// <param name="services">The scope's provider.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when the scope is ready.</returns>
    public ValueTask InitializeScopeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        if (services.GetService<DeliveryStoreHolder>() is { } holder)
        {
            holder.Store = Store;
            holder.Instance = this;
        }

        return services.GetRequiredKeyedService<IMessageStoreInstanceInitializer>(Store)
            .InitializeAsync(Instance, services, ct);
    }

    /// <summary>
    /// Prepares a loop's scope (<see cref="InitializeScopeAsync"/>), then runs the store's check if it is
    /// still pending (D7).
    /// </summary>
    /// <param name="services">The loop scope's provider.</param>
    /// <param name="ct">The host's token.</param>
    /// <returns>
    /// <see langword="true"/> to go on; <see langword="false"/> when the store was refused: the loop ends.
    /// </returns>
    /// <exception cref="Exception">The initializer, or the check, threw: the instance's own failure.</exception>
    public async ValueTask<bool> PrepareLoopScopeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await InitializeScopeAsync(services, ct).ConfigureAwait(false);
        return Registry.EnsureChecked(this, services);
    }

    /// <inheritdoc />
    public void Dispose() => _stopping.Dispose();
}
