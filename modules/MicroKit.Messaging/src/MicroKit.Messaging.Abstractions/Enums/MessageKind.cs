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
/// <b>Persisted as a string</b> (<c>HasConversion&lt;string&gt;</c>, 32 characters), so inserting
/// a member cannot silently remap existing rows and renaming one is a schema change. Keep the
/// names short: SQLite does not enforce column width and PostgreSQL does, so a name longer than
/// the column would pass the fast suite and fail in production.
/// </para>
/// <para>
/// <b><see cref="Notification"/> is declared first, and that is load-bearing.</b> It is the zero
/// value, so it is what a row predating the column materializes as, and what a fixture that omits
/// the property gets. That default is factually correct rather than convenient: every outbox row
/// written before this column existed came through the domain-event path and carries a
/// notification payload. Reordering these members changes the default.
/// </para>
/// </remarks>
public enum MessageKind
{
    /// <summary>
    /// An in-process notification, dispatched through the MediatR fan-out contributed by
    /// <c>MicroKit.Messaging.MediatR</c>. It never crosses a service boundary, so it has no wire
    /// identity: <see cref="OutboxMessage.ContractName"/> and
    /// <see cref="OutboxMessage.SourceMessageId"/> are both <see langword="null"/> on such a row.
    /// </summary>
    Notification,

    /// <summary>
    /// An integration contract, handed to a transport and addressed by its
    /// <see cref="OutboxMessage.ContractName"/>. It crosses a service boundary, so it is
    /// identified by that stable wire name rather than by the CLR type in
    /// <see cref="OutboxMessage.EventType"/>, which the receiving process cannot resolve.
    /// </summary>
    Contract,
}
