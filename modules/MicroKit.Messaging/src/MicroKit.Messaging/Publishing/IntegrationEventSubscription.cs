namespace MicroKit.Messaging.Publishing;

/// <summary>
/// One contract name this application understands, and the local CLR type it deserializes into.
/// </summary>
/// <remarks>
/// <para>
/// <b>It carries no <c>Source</c>, and the absence is the point.</b> A source names the module that
/// <i>emitted</i> an event; it is written to <c>OutboxMessage.Source</c> at staging and
/// travels as the emitter's identity. A consumer did not emit anything, so it has nothing truthful
/// to put there — and a subscription that structurally cannot carry one cannot put a false value in
/// that column. That is why this is a distinct type rather than an
/// <see cref="IntegrationEventRegistration"/> with a null source.
/// </para>
/// <para>
/// The <see cref="EventType"/> is <i>this process's</i> type for the name, never the producer's.
/// The producer's type does not travel and would be useless if it did; a stable contract name
/// exists precisely so the two ends can hold different types.
/// </para>
/// </remarks>
/// <param name="EventType">The local CLR type the contract deserializes into.</param>
/// <param name="ContractName">
/// The wire contract name, taken from <see cref="IntegrationEventAttribute"/> on
/// <paramref name="EventType"/>.
/// </param>
public sealed record IntegrationEventSubscription(Type EventType, string ContractName);
