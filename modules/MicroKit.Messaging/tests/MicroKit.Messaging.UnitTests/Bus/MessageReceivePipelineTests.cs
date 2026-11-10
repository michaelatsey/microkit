namespace MicroKit.Messaging.UnitTests.Bus;

using MicroKit.Messaging.Bus;

/// <summary>
/// The receive pipeline's verdicts (#186, decisions 7 and 8), driven directly so each verdict is
/// asserted rather than inferred from what a transport did with it.
/// </summary>
public sealed class MessageReceivePipelineTests
{
    public static TheoryData<Exception, string> SingleHandlerVerdicts => new()
    {
        { new PermanentMessageException("contract violated"), nameof(DeliveryVerdict.Permanent) },
        { new InboxPayloadException("unreadable"), nameof(DeliveryVerdict.Permanent) },
        { new InboxDependencyUnavailableException("db down"), nameof(DeliveryVerdict.DependencyUnavailable) },
        { new InvalidOperationException("boom"), nameof(DeliveryVerdict.Transient) },
        { new TimeoutException("slow"), nameof(DeliveryVerdict.Transient) },
        // A handler's OWN cancellation, while the host is running, is an ordinary failure.
        { new OperationCanceledException("handler timeout"), nameof(DeliveryVerdict.Transient) },
        // A disposed scope or provider is shutdown, never a failure.
        { new ObjectDisposedException("IServiceProvider"), nameof(DeliveryVerdict.Unsettled) },
        // A handler's own disposed object, while the host runs, is a failure: classifying it as
        // shutdown would stop the consume loop of a live host.
        { new ObjectDisposedException("HttpClient"), nameof(DeliveryVerdict.Transient) },
    };

    [Theory]
    [MemberData(nameof(SingleHandlerVerdicts))]
    public async Task DeliverAsync_ClassifiesWhatTheHandlerThrew(Exception thrown, string expected)
    {
        await using var provider = Build(script => script.First = thrown);

        var outcome = await Pipeline(provider).DeliverAsync(Envelope());

        outcome.Verdict.ToString().ShouldBe(expected);
    }

    [Fact]
    public async Task DeliverAsync_WhenEveryHandlerSucceeds_Succeeds()
    {
        await using var provider = Build(_ => { });

        (await Pipeline(provider).DeliverAsync(Envelope())).Verdict.ShouldBe(DeliveryVerdict.Succeeded);
        provider.GetRequiredService<BusRecorder>().Steps.ShouldBe(["first", "second"]);
    }

    public static TheoryData<Exception?, Exception?, string> TwoHandlerVerdicts => new()
    {
        { null, new PermanentMessageException("x"), nameof(DeliveryVerdict.Permanent) },
        { new PermanentMessageException("x"), new InvalidOperationException("y"), nameof(DeliveryVerdict.Transient) },
        { new InvalidOperationException("y"), new PermanentMessageException("x"), nameof(DeliveryVerdict.Transient) },
        { new InvalidOperationException("y"), new InboxDependencyUnavailableException("z"), nameof(DeliveryVerdict.DependencyUnavailable) },
        { new InboxDependencyUnavailableException("z"), null, nameof(DeliveryVerdict.DependencyUnavailable) },
    };

    [Theory]
    [MemberData(nameof(TwoHandlerVerdicts))]
    public async Task DeliverAsync_WithTwoHandlers_RunsBothAndReturnsTheMostRedeliverableVerdict(
        Exception? first, Exception? second, string expected)
    {
        await using var provider = Build(script =>
        {
            script.First = first;
            script.Second = second;
        });

        var outcome = await Pipeline(provider).DeliverAsync(Envelope());

        outcome.Verdict.ToString().ShouldBe(expected);
        provider.GetRequiredService<BusRecorder>().Steps.ShouldBe(
            ["first", "second"], "a failing handler does not stop the next one from running");
    }

    [Fact]
    public async Task DeliverAsync_WhenTheHostIsStopping_IsUnsettledAndStopsBeforeTheNextHandler()
    {
        using var stopping = new CancellationTokenSource();
        await using var provider = Build(script =>
            script.FirstAction = () =>
            {
                stopping.Cancel();
                stopping.Token.ThrowIfCancellationRequested();
            });

        var outcome = await Pipeline(provider).DeliverAsync(Envelope(), stopping.Token);

        outcome.Verdict.ShouldBe(DeliveryVerdict.Unsettled);
        provider.GetRequiredService<BusRecorder>().Steps.ShouldBe(
            ["first"], "shutdown stops the delivery at once; the second handler never starts");
    }

    [Fact]
    public async Task DeliverAsync_WhenTheContractIsUnknown_IsPermanentAndReachesNoHandler()
    {
        await using var provider = Build(_ => { });

        var outcome = await Pipeline(provider).DeliverAsync(Envelope(type: "tests.bus.unknown.v1"));

        outcome.Verdict.ShouldBe(DeliveryVerdict.Permanent);
        outcome.Reason.ShouldNotBeNull().ShouldContain("tests.bus.unknown.v1");
        provider.GetRequiredService<BusRecorder>().Steps.ShouldBeEmpty();
    }

    [Fact]
    public async Task DeliverAsync_WhenTheIdIsNotAGuid_IsPermanentAndReachesNoHandler()
    {
        await using var provider = Build(_ => { });

        var outcome = await Pipeline(provider).DeliverAsync(Envelope(id: "order-42"));

        outcome.Verdict.ShouldBe(DeliveryVerdict.Permanent);
        provider.GetRequiredService<BusRecorder>().Steps.ShouldBeEmpty();
    }

    [Fact]
    public async Task DeliverAsync_WhenNoHandlerIsRegistered_AcknowledgesIt()
    {
        await using var provider = Build(_ => { }, registerHandlers: false);

        (await Pipeline(provider).DeliverAsync(Envelope())).Verdict.ShouldBe(DeliveryVerdict.Succeeded);
    }

    [Fact]
    public async Task DeliverAsync_WhenTheEnvelopeHasNoCorrelation_MintsOneSharedByEveryHandler()
    {
        await using var provider = Build(_ => { });

        await Pipeline(provider).DeliverAsync(Envelope(headers: []));

        var correlations = provider.GetRequiredService<ScriptedHandlers>().Contexts
            .Select(c => c.CorrelationId).ToList();
        correlations.Count.ShouldBe(2);
        correlations.ShouldAllBe(c => !string.IsNullOrWhiteSpace(c));
        correlations.Distinct().Count().ShouldBe(1, "one delivery, one chain");
    }

    [Fact]
    public async Task DeliverAsync_WhenAScopeInitializerReportsADependencyUnavailable_ReachesNoHandler()
    {
        await using var provider = Build(_ => { }, compose: bus => bus.AddScopeInitializer<UnavailableInitializer>());

        var outcome = await Pipeline(provider).DeliverAsync(Envelope());

        outcome.Verdict.ShouldBe(DeliveryVerdict.DependencyUnavailable);
        provider.GetRequiredService<BusRecorder>().Steps.ShouldBeEmpty();
    }

    // ── helpers ────────────────────────────────────────────────────────────────────────────────

    private static ServiceProvider Build(
        Action<ScriptedHandlers> script,
        bool registerHandlers = true,
        Action<MessagingBuilder>? compose = null)
    {
        var scripted = new ScriptedHandlers();
        script(scripted);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<BusRecorder>();
        services.AddSingleton(scripted);
        services.AddIntegrationEventContracts(BusTestHost.Source, c => c.Publishes<OrderPlaced>());

        var bus = services.AddMicroKitMessaging();
        if (registerHandlers)
        {
            bus.AddMessageHandler<FirstScriptedHandler, OrderPlaced>()
               .AddMessageHandler<SecondScriptedHandler, OrderPlaced>();
        }

        compose?.Invoke(bus);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static MessageReceivePipeline Pipeline(IServiceProvider provider)
        => provider.GetRequiredService<MessageReceivePipeline>();

    private static MessageEnvelope Envelope(
        string type = "tests.bus.order-placed.v1",
        string? id = null,
        Dictionary<string, string>? headers = null)
        => new()
        {
            Id = id ?? Guid.NewGuid().ToString(),
            Source = BusTestHost.Source,
            Type = type,
            Time = DateTimeOffset.UnixEpoch,
            DataContentType = "application/json",
            Headers = headers ?? new Dictionary<string, string>
            {
                [MessageHeaders.CorrelationId] = Guid.NewGuid().ToString(),
            },
            Body = """{"orderId":"bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"}"""u8.ToArray(),
        };
}

/// <summary>What each of the two scripted handlers does, and what they saw.</summary>
internal sealed class ScriptedHandlers
{
    private readonly List<MessageContext> _contexts = [];

    public Exception? First { get; set; }

    public Exception? Second { get; set; }

    public Action? FirstAction { get; set; }

    public IReadOnlyList<MessageContext> Contexts
    {
        get
        {
            lock (_contexts)
            {
                return [.. _contexts];
            }
        }
    }

    public void Saw(MessageContext context)
    {
        lock (_contexts)
        {
            _contexts.Add(context);
        }
    }
}

internal sealed class FirstScriptedHandler(ScriptedHandlers script, BusRecorder recorder) : IMessageHandler<OrderPlaced>
{
    public ValueTask HandleAsync(OrderPlaced message, MessageContext context, CancellationToken ct = default)
    {
        recorder.Step("first");
        script.Saw(context);
        script.FirstAction?.Invoke();
        return script.First is { } fault ? ValueTask.FromException(fault) : ValueTask.CompletedTask;
    }
}

internal sealed class SecondScriptedHandler(ScriptedHandlers script, BusRecorder recorder) : IMessageHandler<OrderPlaced>
{
    public ValueTask HandleAsync(OrderPlaced message, MessageContext context, CancellationToken ct = default)
    {
        recorder.Step("second");
        script.Saw(context);
        return script.Second is { } fault ? ValueTask.FromException(fault) : ValueTask.CompletedTask;
    }
}

internal sealed class UnavailableInitializer : IMessageScopeInitializer
{
    public ValueTask InitializeAsync(MessageContext context, IServiceProvider services, CancellationToken ct = default)
        => ValueTask.FromException(new InboxDependencyUnavailableException("tenant directory down"));
}
