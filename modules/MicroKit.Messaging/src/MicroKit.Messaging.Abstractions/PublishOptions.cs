namespace MicroKit.Messaging;

/// <summary>Optional settings for one publication through <see cref="IEventPublisher"/>.</summary>
public sealed record PublishOptions
{
    /// <summary>
    /// Gets the tenant the message belongs to, for code that runs outside a message or request.
    /// </summary>
    /// <remarks>
    /// When the ambient tenant is null this value is used as is. When the ambient tenant is set, a
    /// different value here is rejected with <see cref="IntegrationEventPublishException"/> before
    /// anything is sent: a message is never re-attributed to another tenant by a call site.
    /// </remarks>
    public string? TenantId { get; init; }

    /// <summary>
    /// Gets extra headers carried on the envelope. Null means none.
    /// </summary>
    /// <remarks>
    /// The reserved headers — <c>tenantid</c>, <c>correlationid</c>, <c>causationid</c> and
    /// <c>traceparent</c> — are set by the bus and cannot be supplied here: naming one throws
    /// <see cref="ArgumentException"/>.
    /// </remarks>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>
    /// Gets the store whose outbox the publication is enlisted in. Null lets the bus choose.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The bus chooses a store, first match wins: this value when set; inside a scope the engine
    /// opened for a delivery, that delivery's store; the only configured store; the store bound to
    /// the message type's assembly. With no match, the publication fails with
    /// <see cref="IntegrationEventConfigurationException"/>.
    /// </para>
    /// <para>
    /// The value must name a configured store, and inside a delivery it must name that delivery's
    /// store; otherwise <see cref="IntegrationEventPublishException"/> is thrown and nothing is
    /// enlisted. With no store configured, setting it is an error too.
    /// </para>
    /// </remarks>
    public MessageStoreKey? Store { get; init; }
}
