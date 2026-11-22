namespace MicroKit.Messaging.Options;

/// <summary>
/// The engine's options for the instances of partitioned stores (#191), section
/// <c>MicroKit:Messaging:Instances</c>. A single store ignores them.
/// </summary>
public sealed record StoreInstanceOptions
{
    /// <summary>
    /// Gets or sets how many outbox and inbox batches may be in progress at once — from claim to
    /// settlement — across every instance of every partitioned store. Waiting batches are admitted in
    /// arrival order, so an instance with a large backlog cannot starve another. At least 1; 16 by
    /// default.
    /// </summary>
    /// <remarks>
    /// It bounds batches, not connections. A database driver keeps one pool per connection string — one
    /// per database — and an idle connection stays pooled for a while (Npgsql: <c>Connection Idle
    /// Lifetime</c>, 300 s by default), so fifty databases each keeping a few idle connections can exceed
    /// a server's <c>max_connections</c> (100 by default on PostgreSQL) whatever this value. A host with a
    /// database per tenant sizes its pools (<c>Maximum Pool Size</c>, <c>Connection Idle Lifetime</c>) or
    /// puts a pooler in front of the server.
    /// </remarks>
    public int MaxConcurrentBatches { get; set; } = 16;

    /// <summary>
    /// Gets or sets a value indicating whether every instrument tagged <c>store.key</c> also carries
    /// <c>store.instance</c>, the instance's id. Off by default: with a database per tenant, the instance
    /// count is the tenant count. Off, a partitioned store reports one series per <c>store.key</c> (and
    /// <c>endpoint</c>), its back-off counters summed over its instances.
    /// </summary>
    public bool TagInstances { get; set; }
}
