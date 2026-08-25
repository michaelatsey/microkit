namespace MicroKit.Messaging.Publishing;

/// <summary>One event type, the contract name it is published under, and its emitting module.</summary>
/// <param name="EventType">The CLR type of the integration event.</param>
/// <param name="ContractName">
/// The wire contract name, taken from <see cref="IntegrationEventAttribute"/>.
/// </param>
/// <param name="Source">
/// The emitting module, supplied per module at registration rather than per host — so several
/// modules composed into one process each keep their own identity.
/// </param>
public sealed record IntegrationEventRegistration(Type EventType, string ContractName, string Source);
