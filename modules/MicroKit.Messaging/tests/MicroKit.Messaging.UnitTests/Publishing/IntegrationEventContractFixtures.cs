namespace MicroKit.Messaging.UnitTests.Publishing;

using MicroKit.Messaging.Publishing;

/// <summary>
/// Builds a registry the way an application does — through <c>AddMicroKitMessaging(bus => ...)</c>
/// — so the tests exercise the real composition path rather than a shape only they can produce.
/// </summary>
internal static class IntegrationEventContractFixtures
{
    internal const string ServiceName = "tests.registry";

    /// <summary>Composes one <c>AddMicroKitMessaging</c> call and resolves the registry.</summary>
    internal static IntegrationEventRegistry Registry(Action<MessagingBuilder> bus)
        => Registry([bus]);

    /// <summary>
    /// Composes several <c>AddMicroKitMessaging</c> calls — one per module of a monolith — and
    /// resolves the registry. The first call names the service unless it sets a name itself.
    /// </summary>
    internal static IntegrationEventRegistry Registry(IReadOnlyList<Action<MessagingBuilder>> calls)
    {
        var services = new ServiceCollection();
        for (var i = 0; i < calls.Count; i++)
        {
            var call = calls[i];
            var first = i == 0;
            services.AddMicroKitMessaging(bus =>
            {
                if (first)
                {
                    bus.ServiceName = ServiceName;
                }

                call(bus);
            });
        }

        return services.BuildServiceProvider().GetRequiredService<IntegrationEventRegistry>();
    }
}

[MessageContract("saasbtp.safety.constat-recorded")]
internal sealed record ConstatRecorded(Guid ConstatId) : IIntegrationEvent;

[MessageContract("saasbtp.access.workspace-created")]
internal sealed record WorkspaceCreated(Guid WorkspaceId) : IIntegrationEvent;

/// <summary>Claims a contract name that already belongs to <see cref="ConstatRecorded"/>.</summary>
[MessageContract("saasbtp.safety.constat-recorded")]
internal sealed record ImpostorEvent(Guid Whatever) : IIntegrationEvent;

/// <summary>The same contract name as <see cref="ConstatRecorded"/>, in its second version.</summary>
[MessageContract("saasbtp.safety.constat-recorded", Version = 2)]
internal sealed record ConstatRecordedV2(Guid ConstatId, string Severity) : IIntegrationEvent;

/// <summary>
/// A contract emitted by another service. This process handles it and never publishes it.
/// </summary>
[MessageContract("partner.billing.invoice-settled")]
internal sealed record InvoiceSettled(Guid InvoiceId) : IIntegrationEvent;

/// <summary>Carries no <c>[MessageContract]</c> attribute, deliberately.</summary>
internal sealed record NeverRegistered : IIntegrationEvent;

/// <summary>
/// Carries a wire identity that is not one. The attribute applies and compiles; it throws when the
/// attribute is materialized, which is inside the declaration that names this type.
/// </summary>
[MessageContract("")]
internal sealed record BlankContractName : IIntegrationEvent;

/// <summary>
/// A rejected PROPERTY rather than a constructor argument: reflection wraps the setter's exception
/// in a <c>CustomAttributeFormatException</c> when the attribute is materialized.
/// </summary>
[MessageContract("saasbtp.safety.bad-version", Version = 0)]
internal sealed record BadVersionContract : IIntegrationEvent;

/// <summary>Whitespace is the same defect wearing a disguise the compiler will not strip.</summary>
[MessageContract("   ")]
internal sealed record WhitespaceContractName : IIntegrationEvent;

internal sealed class ConstatRecordedHandler : IMessageHandler<ConstatRecorded>
{
    public ValueTask HandleAsync(ConstatRecorded message, MessageContext context, CancellationToken ct = default)
        => ValueTask.CompletedTask;
}

internal sealed class SecondConstatRecordedHandler : IMessageHandler<ConstatRecorded>
{
    public ValueTask HandleAsync(ConstatRecorded message, MessageContext context, CancellationToken ct = default)
        => ValueTask.CompletedTask;
}

internal sealed class ImpostorHandler : IMessageHandler<ImpostorEvent>
{
    public ValueTask HandleAsync(ImpostorEvent message, MessageContext context, CancellationToken ct = default)
        => ValueTask.CompletedTask;
}

internal sealed class InvoiceSettledHandler : IMessageHandler<InvoiceSettled>
{
    public ValueTask HandleAsync(InvoiceSettled message, MessageContext context, CancellationToken ct = default)
        => ValueTask.CompletedTask;
}

internal sealed class NeverRegisteredHandler : IMessageHandler<NeverRegistered>
{
    public ValueTask HandleAsync(NeverRegistered message, MessageContext context, CancellationToken ct = default)
        => ValueTask.CompletedTask;
}

internal sealed class WhitespaceContractNameHandler : IMessageHandler<WhitespaceContractName>
{
    public ValueTask HandleAsync(WhitespaceContractName message, MessageContext context, CancellationToken ct = default)
        => ValueTask.CompletedTask;
}
