namespace MicroKit.Messaging.Outbox;

/// <summary>
/// Scoped slot naming the outbox row whose dispatch is currently running in this scope.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it is for.</b> A notification handler reached through the outbox may publish an
/// integration event, and that contract row must record which row's dispatch produced it —
/// <see cref="OutboxMessage.OriginMessageId"/>, half of the replay key on
/// (<c>OriginMessageId</c>, <c>ContractName</c>). The publisher is several frames below
/// <c>OutboxProcessor</c>, behind <c>IPublisher.Publish</c> and a handler, so the identity has to
/// travel through the scope rather than through a parameter.
/// </para>
/// <para>
/// <b>Written by <c>OutboxProcessor</c> on the scope it received, not by the scope factory.</b>
/// That is deliberate: a host may supply its own <see cref="IExecutionScopeFactory"/>, and a
/// mechanism that depended on every implementation remembering to carry a value would fail
/// silently — a null origin does not throw, it switches deduplication off, because nulls are
/// distinct in the unique index. The processor writes the holder itself, so no factory can drop it.
/// </para>
/// <para>
/// <b>Not <c>IExecutionContext.Properties</c>, for two reasons.</b> That is a Level 0 contract
/// whose own documentation forbids messaging types leaking into it, so an outbox row id could only
/// travel there as a stringly-typed key; and a custom factory that rebuilds the context without
/// copying the bag reintroduces exactly the silent loss described above. A dedicated scoped holder
/// keeps the notion in the package that owns it, and mirrors
/// <see cref="MicroKit.Messaging.Execution.ExecutionContextHolder"/>, which exists for the same
/// structural reason — Microsoft DI activates constructor dependencies from its own scope, so a
/// value has to be resolvable as a service to reach constructor injection at all.
/// </para>
/// <para>
/// <b>Null is a valid and common value.</b> Publishing from a command handler or a scheduled job
/// happens outside any dispatch, so nothing writes this and the contract row carries no origin.
/// Such a row does not deduplicate, and for those two callers that is correct: the replay key
/// guards the replay of a dispatch, and an HTTP request is not replayed by anything.
/// </para>
/// <para>
/// <b>An inbox handler is not a third example of that, and the difference matters.</b> An inbox
/// handler replay <i>is</i> a replay — at-least-once delivery guarantees nothing else. What makes a
/// null origin safe there is not the absence of a replay but <see cref="IInboxSettlementStore"/>:
/// the processed mark is staged into the handler's own unit of work, so the mark, the handler's
/// side effects and any contract row it published commit together or roll back together. There is
/// no window in which the contract survives while the row stays claimable.
/// </para>
/// <para>
/// That distinction is worth keeping straight because the two justifications fail differently. If
/// per-message settlement is ever relaxed — batched like the outbox's, say — the "outside a
/// dispatch there is no replay" reading gives no warning, while the real mechanism names exactly
/// what would have been removed. Nothing produces inbox rows in this release (ADR-MSG-019), so this
/// is a note for whoever ships the receiving seam: if a contract published by an inbox handler must
/// deduplicate, its origin is that handler's <c>InboxMessage.MessageId</c>, and it wants a holder
/// stamped by <c>InboxProcessor</c> the way this one is stamped by <c>OutboxProcessor</c>.
/// </para>
/// </remarks>
internal sealed class OriginMessageHolder
{
    /// <summary>
    /// Gets or sets the identifier of the outbox row being dispatched in this scope, or
    /// <see langword="null"/> when this scope is not an outbox dispatch.
    /// </summary>
    public MessageId? OriginMessageId { get; set; }
}
