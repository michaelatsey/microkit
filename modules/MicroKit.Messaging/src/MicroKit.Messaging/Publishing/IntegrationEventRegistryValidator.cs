namespace MicroKit.Messaging.Publishing;

/// <summary>
/// Forces <see cref="IntegrationEventRegistry"/> to be composed at startup rather than lazily.
/// </summary>
/// <remarks>
/// <para>
/// The registry is a factory-built singleton, so without this it would be constructed on first
/// resolve — which on this path is the first publication, inside a notification handler, inside a
/// transaction. A duplicated contract name would roll that transaction back and be classified as a
/// transient failure, retrying forever against something no retry can fix. Resolving it at boot
/// converts that into a startup failure, which is what it is.
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
    /// Two modules declare the same contract name, or one type is declared twice.
    /// </exception>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = serviceProvider.GetRequiredService<IntegrationEventRegistry>();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
