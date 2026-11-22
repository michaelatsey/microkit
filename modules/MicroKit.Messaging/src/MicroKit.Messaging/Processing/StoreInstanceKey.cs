namespace MicroKit.Messaging.Processing;

/// <summary>
/// Names the state of one drained unit of persistence: a single store (<see cref="Instance"/> null, the
/// store itself) or one instance of a partitioned store (#191).
/// </summary>
/// <param name="Store">The store's key, bounded by the configured stores.</param>
/// <param name="Instance">The instance's id within its store; <see langword="null"/> for a single store.</param>
internal readonly record struct StoreInstanceKey(MessageStoreKey Store, string? Instance);
