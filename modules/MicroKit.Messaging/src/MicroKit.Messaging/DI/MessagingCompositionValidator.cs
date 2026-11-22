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
/// a configured store missing one of the services the engine resolves keyed by its key;
/// </description></item>
/// <item><description>
/// the start checks a store provider added (for EF Core: the <c>DbContext</c> is not registered,
/// or its model lacks the messaging entities).
/// </description></item>
/// </list>
/// <para>
/// <b>The registration checks are structural.</b> They ask <see cref="IServiceProviderIsService"/>
/// whether a service is registered and resolve nothing, so they prove a registration exists, not
/// that its activation will succeed: a store whose constructor throws is reported by the engine as
/// an activation failure, where it is first resolved. A store provider's own checks may resolve
/// what they need.
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
    IServiceProvider services,
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

        var isService = services.GetRequiredService<IServiceProviderIsService>();
        if (composition.Publications.Count > 0 && !isService.IsService(typeof(IMessageTransport)))
        {
            var types = string.Join(", ", composition.Publications.Select(t => $"'{t.Name}'"));
            problems.Add(
                $"{types} {(composition.Publications.Count == 1 ? "is" : "are")} declared with " +
                "bus.Publishes<T>(), but no IMessageTransport is registered, so a publication has " +
                "no route. Call bus.UseInMemoryTransport(), or register a broker provider's transport.");
        }

        var keyed = services.GetRequiredService<IServiceProviderIsKeyedService>();
        foreach (var store in composition.Stores)
        {
            var missing = StoreEngineRegistration.RequiredStoreServices
                .Where(required => !keyed.IsKeyedService(required, store.Key))
                .Select(required => required.Name)
                .ToList();
            if (missing.Count > 0)
            {
                problems.Add(
                    $"Store '{store.Key}' is configured, but {string.Join(", ", missing)} " +
                    $"{(missing.Count == 1 ? "is" : "are")} not registered under its key, so the " +
                    "engine cannot drain it. Its provider registers every store service keyed by " +
                    "the store's key: fix the provider's registration.");
            }

            if (store.PartitionCalls > 1)
            {
                problems.Add(
                    $"Store '{store.Key}' is given an instance source {store.PartitionCalls} times; a " +
                    "partitioned store has one. Call PartitionedBy once for it.");
            }

            foreach (var check in store.StartupChecks)
            {
                problems.AddRange(check(services));
            }
        }

        foreach (var check in composition.StartupChecks)
        {
            problems.AddRange(check(services));
        }

        problems.AddRange(UnboundLocalConsumers());

        MessagingCompositionAnalysis.ThrowIfAny(problems);
    }

    /// <summary>
    /// The local consumers are read at start, so a source supplying an invalid one fails here rather
    /// than a send. With several stores, a local consumer whose handler type sits in an assembly bound
    /// to none of them has no inbox to be guarded by — the rule an unbound handler already follows (D12).
    /// </summary>
    private IEnumerable<string> UnboundLocalConsumers()
    {
        if (composition.LocalConsumerSources.Count == 0)
        {
            return [];
        }

        IReadOnlyList<LocalConsumer> consumers;
        try
        {
            consumers = services.GetRequiredService<Registry.LocalConsumerRegistry>().Consumers;
        }
        catch (ArgumentException ex)
        {
            return [$"A local consumer source supplied an invalid consumer: {ex.Message} Fix the source's consumers."];
        }

        return composition.Stores.Count < 2 ? [] : Unbound(consumers);
    }

    private IEnumerable<string> Unbound(IReadOnlyList<LocalConsumer> consumers)
    {
        var bound = composition.Stores.SelectMany(s => s.Assemblies).ToHashSet();
        foreach (var handler in consumers.Select(c => c.HandlerType).Distinct())
        {
            if (!bound.Contains(handler.Assembly))
            {
                yield return
                    $"Local consumer '{handler.FullName}' is bound to no store, and {composition.Stores.Count} " +
                    $"are configured. Bind its assembly '{handler.Assembly.GetName().Name}' to one store " +
                    "(for EF Core: store => store.ForAssemblyContaining<T>()).";
            }
        }
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
