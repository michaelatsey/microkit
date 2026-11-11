namespace MicroKit.Messaging;

/// <summary>
/// Validates the messaging composition when the host starts, before any worker runs, and fails
/// the start with one <see cref="IntegrationEventConfigurationException"/> listing every problem.
/// </summary>
/// <remarks>
/// <para>
/// <b>It holds no rule of its own.</b> The contract rules come from
/// <see cref="MessagingCompositionAnalysis"/>, the same analysis the registry applies. To them it
/// adds the checks that need the container:
/// </para>
/// <list type="bullet">
/// <item><description>a declared publication with no <see cref="IMessageTransport"/> registered;</description></item>
/// <item><description>
/// an <see cref="IIntegrationEventPublisher"/> registered — publications staged in the outbox —
/// with no transport dispatcher to send them: the outbox would stop on its first contract row;
/// </description></item>
/// <item><description>
/// an <see cref="IOutboxProcessorStore"/> or <see cref="IInboxProcessorStore"/> registered while
/// the matching engine — <c>AddOutboxInboxProcessing()</c> — is not: staged rows would never be
/// drained.
/// </description></item>
/// </list>
/// <para>
/// <b>Those are structural checks.</b> They ask <see cref="IServiceProviderIsService"/> whether a
/// service is registered and resolve nothing, so they prove a registration exists, not that its
/// activation will succeed: a transport whose constructor throws still fails later, where it is
/// first resolved.
/// </para>
/// <para>
/// <b>Why <see cref="StartingAsync"/>.</b> The host runs every <c>StartingAsync</c> before any
/// <c>StartAsync</c>, so the validation precedes every worker whatever the registration order.
/// <c>AddMicroKitMessaging</c> registers this service before running its lambda, so it also
/// precedes the in-memory transport's own <c>StartingAsync</c>, where the endpoint is bound.
/// <b>The binding does not rely on that order</b>: the in-memory transport runs
/// <see cref="Validate"/> itself before it binds, so an invalid composition binds nothing even
/// under <c>HostOptions.ServicesStartConcurrently</c>.
/// </para>
/// </remarks>
internal sealed class MessagingCompositionValidator(
    MessagingComposition composition,
    IServiceProviderIsService isService,
    ILogger<MessagingCompositionValidator> logger)
    : IHostedLifecycleService
{
    /// <inheritdoc />
    /// <exception cref="IntegrationEventConfigurationException">The composition is invalid.</exception>
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        Validate();
        ReportWarnings();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Logs what is worth knowing but is no failure: one <c>Warning</c> per assembly a handler
    /// scan could load only in part (event 3016).
    /// </summary>
    internal void ReportWarnings()
    {
        foreach (var unloaded in composition.Unloaded)
        {
            Bus.BusLogs.TypesNotLoaded(
                logger,
                unloaded.Assembly,
                unloaded.Count,
                unloaded.TypeNames.Count > 0 ? string.Join(", ", unloaded.TypeNames) : "names not reported",
                unloaded.FirstError);
        }
    }

    /// <summary>Runs every check and throws once, listing every problem found.</summary>
    /// <exception cref="IntegrationEventConfigurationException">The composition is invalid.</exception>
    internal void Validate()
    {
        var problems = new List<string>(MessagingCompositionAnalysis.Analyze(composition));

        if (composition.Publications.Count > 0 && !isService.IsService(typeof(IMessageTransport)))
        {
            var types = string.Join(", ", composition.Publications.Select(t => $"'{t.Name}'"));
            problems.Add(
                $"{types} {(composition.Publications.Count == 1 ? "is" : "are")} declared with " +
                "bus.Publishes<T>(), but no IMessageTransport is registered, so a publication has " +
                "no route. Call bus.UseInMemoryTransport(), or register a broker provider's transport.");
        }

        if (isService.IsService(typeof(IIntegrationEventPublisher))
            && !(isService is IServiceProviderIsKeyedService keyed
                 && keyed.IsKeyedService(typeof(IOutboxDispatcher), OutboxDispatcherKeys.Standard)))
        {
            problems.Add(
                "IIntegrationEventPublisher stages publications in the outbox, but no transport " +
                "dispatcher is registered to send them, so the outbox would stop on its first " +
                "contract row. Register the transport through bus.UseInMemoryTransport(), or a " +
                "broker provider's transport registration.");
        }

        if (isService.IsService(typeof(IOutboxProcessorStore)) && !isService.IsService(typeof(IOutboxProcessor)))
        {
            problems.Add(
                "An IOutboxProcessorStore is registered, but the outbox engine is not, so staged rows " +
                "would never be dispatched. Register the stores through AddEfCoreOutbox<TContext>(), " +
                "or call AddOutboxInboxProcessing() after registering your own.");
        }

        if (isService.IsService(typeof(IInboxProcessorStore)) && !isService.IsService(typeof(IInboxProcessor)))
        {
            problems.Add(
                "An IInboxProcessorStore is registered, but the inbox engine is not, so received rows " +
                "would never be handled. Register the stores through AddEfCoreOutbox<TContext>(), " +
                "or call AddOutboxInboxProcessing() after registering your own.");
        }

        MessagingCompositionAnalysis.ThrowIfAny(problems);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
