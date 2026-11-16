namespace MicroKit.Messaging.Spi;

/// <summary>The state of an inbox row, as <see cref="IInboxGuardStore.FindAsync"/> read it.</summary>
/// <remarks>
/// Part of the SPI (<c>MicroKit.Messaging.Spi</c>): a store returns it, the engine reads it.
/// </remarks>
/// <param name="Status">The row's status.</param>
/// <param name="DeadLettered">Whether the row is dead-lettered.</param>
/// <param name="RowId">
/// The row's surrogate key, so the engine recognises the row an attempt of its own wrote — one whose
/// commit landed although the attempt raised — from one a concurrent delivery wrote.
/// </param>
public readonly record struct InboxRowState(InboxMessageStatus Status, bool DeadLettered, Guid RowId);
