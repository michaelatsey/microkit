namespace MicroKit.Messaging.Publishing;

/// <summary>
/// Forces <see cref="IntegrationEventRegistry"/> to be composed at startup rather than lazily.
/// </summary>
/// <remarks>
/// <para>
/// The registry is a factory-built singleton, so without this it would be constructed on first
/// resolve — which on the publishing path is the first publication, inside a notification handler,
/// inside a transaction, and on the consuming path the first message drained. A collision would
/// roll that transaction back and be classified as a transient failure, retrying forever against
/// something no retry can fix. Resolving it at boot converts that into a startup failure, which is
/// what it is.
/// </para>
/// <para>
/// <b>It covers both directions</b>, because the registry validates both: a contract name claimed
/// by two publishers, one type published twice, and a name bound to two different local types —
/// whether the rival claim comes from a publisher or a subscriber. Registered by
/// <c>AddIntegrationEventPublishing()</c> and by <c>AddIntegrationEventConsumption()</c>, so a
/// service that only consumes gets the same guarantee as one that publishes; calling both yields a
/// single validator.
/// </para>
/// <para>
/// It takes <see cref="IServiceProvider"/> and resolves in <see cref="StartAsync"/> rather than
/// taking the registry by constructor, deliberately: the validation must be an observable action
/// at a known point in the lifecycle, not a side effect of this type happening to be activated.
/// That also makes the guarantee directly testable without standing up a host.
/// </para>
/// </remarks>
internal sealed class IntegrationEventRegistryValidator(IServiceProvider serviceProvider)
    : IHostedService
{
    /// <inheritdoc />
    /// <exception cref="IntegrationEventConfigurationException">
    /// Two types claim one contract name — whether both publish it, both consume it, or one of
    /// each — or one type is published twice.
    /// </exception>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = serviceProvider.GetRequiredService<IntegrationEventRegistry>();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
