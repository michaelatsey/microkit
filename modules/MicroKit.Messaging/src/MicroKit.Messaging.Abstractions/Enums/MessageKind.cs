namespace MicroKit.Messaging;

/// <summary>
/// The nature of an <see cref="OutboxMessage"/>, and the routing decision taken from it.
/// </summary>
/// <remarks>
/// <para>
/// The outbox is <b>reentrant</b>: one table carries both natures, and a message can pass through
/// the queue twice. A domain event is staged as a <see cref="Notification"/> and fanned out in
/// process; a handler in that fan-out may publish an integration event, which is staged as a
/// <see cref="Contract"/> and handed to a transport on the second pass.
/// </para>
/// <para>
/// <b>Declared on the row, never inferred from the payload's CLR type.</b> A type test is
/// invisible to SQL — an operator cannot ask how many contracts are stuck — and it forces the
/// payload-agnostic core to know about the notification abstraction it deliberately does not
/// reference.
/// </para>
/// <para>
/// <b>Both dispatchers read this column, and neither performs a type test.</b>
/// <c>TransportOutboxDispatcher</c> switches on it to build an envelope or to refuse the row;
/// <c>MediatROutboxDispatcher</c> switches on it to publish in process or to delegate inward
/// without deserializing at all. That a <see cref="Contract"/> row is delegated even when its
/// payload happens to be an <c>INotification</c> is pinned by
/// <c>DispatchAsync_WhenKindIsContract_AndPayloadIsANotification_StillDelegates</c>, which exists
/// specifically to kill a CLR-type router should one ever come back.
/// </para>
/// <para>
/// <b>Persisted as a string</b> (<c>HasConversion&lt;string&gt;</c>, 32 characters), so inserting
/// a member cannot silently remap existing rows and renaming one is a schema change. Keep the
/// names short: SQLite does not enforce column width and PostgreSQL does, so a name longer than
/// the column would pass the fast suite and fail in production.
/// </para>
/// <para>
/// <b><see cref="Notification"/> is the zero value, and that is load-bearing.</b> It is what a
/// writer that omits the property gets. No writer shipped here omits it — <c>OutboxMessageFactory</c>
/// sets it explicitly on both paths, so the zero value is a safety net rather than the mechanism —
/// but it still governs any row written by a consumer's own code or carried over by a migration.
/// The default is factually correct rather than convenient: a row written without stating
/// a kind came through the domain-event path and carries a notification payload. The members carry
/// explicit values so that this survives a reordering of the declarations instead of depending on
/// one.
/// </para>
/// <para>
/// Backfilling an existing table is a <i>different</i> mechanism, and the two are easy to conflate.
/// The column stores a string, so a row predating it reads <c>'Notification'</c> from the
/// migration's <c>DEFAULT</c> clause — not from the CLR zero value, which never reaches a row
/// nobody wrote. The migration is in the module CHANGELOG.
/// </para>
/// </remarks>
public enum MessageKind
{
    /// <summary>
    /// An in-process notification, dispatched through the MediatR fan-out contributed by
    /// <c>MicroKit.Messaging.MediatR</c>. It never crosses a service boundary, so it has no wire
    /// identity: <see cref="OutboxMessage.ContractName"/> and
    /// <see cref="OutboxMessage.OriginMessageId"/> are both <see langword="null"/> on such a row.
    /// </summary>
    Notification = 0,

    /// <summary>
    /// An integration contract, handed to a transport and addressed by its
    /// <see cref="OutboxMessage.ContractName"/>. It crosses a service boundary, so it is
    /// identified by that stable wire name rather than by the CLR type in
    /// <see cref="OutboxMessage.EventType"/>, which the receiving process cannot resolve.
    /// </summary>
    Contract = 1,
}
