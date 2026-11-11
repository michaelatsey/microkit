namespace MicroKit.Messaging.UnitTests.Bus;

using System.Diagnostics;
using MicroKit.Messaging.Execution;
using Microsoft.Extensions.Time.Testing;
using MessageCtx = MicroKit.Messaging.Execution.ExecutionContext;

/// <summary>
/// The publish side of the bus, against a recording transport: every check runs before anything
/// is sent, and the reserved headers come from the ambient context, never from a caller or a
/// middleware.
/// </summary>
public sealed class MessageBusTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PublishAsync_BuildsTheEnvelopeFromTheContractTheClockAndTheSerializer()
    {
        var (provider, transport) = Build();
        await using var scope = provider.CreateAsyncScope();

        var id = await scope.ServiceProvider.GetRequiredService<IEventPublisher>()
            .PublishAsync(new OrderPlaced(Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa")));

        var envelope = transport.Sent.ShouldHaveSingleItem();
        envelope.Id.ShouldBe(id.ToString());
        envelope.Type.ShouldBe("tests.bus.order-placed.v1");
        envelope.Source.ShouldBe(BusTestHost.Source);
        envelope.Time.ShouldBe(Now);
        envelope.DataContentType.ShouldBe("application/json");
        envelope.BodyText().ShouldBe("""{"orderId":"aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"}""");
    }

    [Fact]
    public async Task IMessageBusAndIEventPublisher_AreOneScopedInstance()
    {
        var (provider, _) = Build();
        await using var scope = provider.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IMessageBus>()
            .ShouldBeSameAs(scope.ServiceProvider.GetRequiredService<IEventPublisher>());
    }

    [Fact]
    public async Task PublishAsync_WhenTheExplicitTenantContradictsTheAmbientOne_ThrowsAndSendsNothing()
    {
        var (provider, transport) = Build();
        await using var scope = await MessageScopeAsync(provider, tenant: "tenant-a");

        var ex = await Should.ThrowAsync<IntegrationEventPublishException>(async () =>
            await Bus(scope).PublishAsync(
                new OrderPlaced(Guid.NewGuid()), new PublishOptions { TenantId = "tenant-b" }));

        ex.Message.ShouldContain("tenant-a");
        ex.Message.ShouldContain("tenant-b");
        transport.Sent.ShouldBeEmpty("an explicit tenant is rejected before anything is sent");
    }

    [Fact]
    public async Task PublishAsync_WhenTheExplicitTenantMatchesTheAmbientOne_Sends()
    {
        var (provider, transport) = Build();
        await using var scope = await MessageScopeAsync(provider, tenant: "tenant-a");

        await Bus(scope).PublishAsync(
            new OrderPlaced(Guid.NewGuid()), new PublishOptions { TenantId = "tenant-a" });

        transport.Sent.ShouldHaveSingleItem().Header(MessageHeaders.TenantId).ShouldBe("tenant-a");
    }

    [Fact]
    public async Task PublishAsync_OutsideAMessage_HasNoAmbientTenant_AndSendsNoTenantHeader()
    {
        var (provider, transport) = Build();
        await using var scope = provider.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(new OrderPlaced(Guid.NewGuid()));

        var envelope = transport.Sent.ShouldHaveSingleItem();
        envelope.Header(MessageHeaders.TenantId).ShouldBeNull();
        envelope.Header(MessageHeaders.CausationId).ShouldBeNull();
        envelope.Header(MessageHeaders.CorrelationId).ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task PublishAsync_WithNoTransport_ThrowsAConfigurationFault()
    {
        var (provider, _) = Build(registerTransport: false);
        await using var scope = provider.CreateAsyncScope();

        var ex = await Should.ThrowAsync<IntegrationEventConfigurationException>(async () =>
            await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(new OrderPlaced(Guid.NewGuid())));

        ex.Message.ShouldContain("UseInMemoryTransport()");
    }

    [Fact]
    public async Task PublishAsync_WhenTheTypeIsNotARegisteredContract_ThrowsAConfigurationFault()
    {
        var (provider, transport) = Build();
        await using var scope = provider.CreateAsyncScope();

        await Should.ThrowAsync<IntegrationEventConfigurationException>(async () =>
            await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(new Unregistered()));

        transport.Sent.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(MessageHeaders.TenantId)]
    [InlineData(MessageHeaders.CorrelationId)]
    [InlineData(MessageHeaders.CausationId)]
    [InlineData(MessageHeaders.TraceParent)]
    [InlineData("TenantId")]
    public async Task PublishAsync_WhenOptionsNameAReservedHeader_ThrowsAndSendsNothing(string header)
    {
        var (provider, transport) = Build();
        await using var scope = provider.CreateAsyncScope();

        var ex = await Should.ThrowAsync<ArgumentException>(async () =>
            await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(
                new OrderPlaced(Guid.NewGuid()),
                new PublishOptions { Headers = new Dictionary<string, string> { [header] = "x" } }));

        ex.Message.ShouldContain(header);
        transport.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task PublishAsync_ReservedHeadersSetByASendMiddleware_AreOverwrittenByTheAmbientContext()
    {
        // A middleware must not be able to forge the tenant — nor the chain or the trace. The bus
        // writes the reserved headers AFTER the last step.
        var (provider, transport) = Build(compose: bus => bus.AddSendMiddleware<ForgingSendMiddleware>());
        await using var scope = await MessageScopeAsync(
            provider, tenant: "tenant-real", correlation: "corr-real", causation: "cause-real");

        using var activity = new Activity("publish").Start();
        await Bus(scope).PublishAsync(new OrderPlaced(Guid.NewGuid()));

        var envelope = transport.Sent.ShouldHaveSingleItem();
        envelope.Header(MessageHeaders.TenantId).ShouldBe("tenant-real");
        envelope.Header(MessageHeaders.CorrelationId).ShouldBe("corr-real");
        envelope.Header(MessageHeaders.CausationId).ShouldBe("cause-real");
        envelope.Header(MessageHeaders.TraceParent).ShouldBe(activity.Id);
        envelope.Header("x-kept").ShouldBe("kept", "non-reserved middleware headers travel");
        envelope.Headers.Keys
            .Count(k => string.Equals(k, MessageHeaders.TenantId, StringComparison.OrdinalIgnoreCase))
            .ShouldBe(1, "a differently cased 'TenantId' must not ride beside the real one");
    }

    [Fact]
    public async Task PublishAsync_ReservedHeadersSetByASendMiddleware_AreRemovedWhenTheContextHasNone()
    {
        // Overwriting includes overwriting with absence: no ambient tenant, causation or trace
        // means no header, whatever the middleware wrote.
        var (provider, transport) = Build(compose: bus => bus.AddSendMiddleware<ForgingSendMiddleware>());
        await using var scope = provider.CreateAsyncScope();
        Activity.Current = null;

        await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(new OrderPlaced(Guid.NewGuid()));

        var envelope = transport.Sent.ShouldHaveSingleItem();
        envelope.Header(MessageHeaders.TenantId).ShouldBeNull();
        envelope.Header(MessageHeaders.CausationId).ShouldBeNull();
        envelope.Header(MessageHeaders.TraceParent).ShouldBeNull();
        envelope.Header(MessageHeaders.CorrelationId).ShouldNotBe("forged");
    }

    [Fact]
    public async Task PublishAsync_WhenASendMiddlewareDoesNotProceed_ThrowsRatherThanSucceedingSilently()
    {
        var (provider, transport) = Build(compose: bus => bus.AddSendMiddleware<SwallowingSendMiddleware>());
        await using var scope = provider.CreateAsyncScope();

        await Should.ThrowAsync<IntegrationEventPublishException>(async () =>
            await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(new OrderPlaced(Guid.NewGuid())));

        transport.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task PublishAsync_RunsSendMiddlewareInRegistrationOrder()
    {
        var (provider, _) = Build(compose: bus => bus
            .AddSendMiddleware<FirstOrderedMiddleware>()
            .AddSendMiddleware<SecondOrderedMiddleware>());
        await using var scope = provider.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(new OrderPlaced(Guid.NewGuid()));

        provider.GetRequiredService<BusRecorder>().Steps.ShouldBe(["first", "second"]);
    }

    // ── helpers ────────────────────────────────────────────────────────────────────────────────

    private static (ServiceProvider Provider, RecordingMessageTransport Transport) Build(
        bool registerTransport = true, Action<MessagingBuilder>? compose = null)
    {
        var transport = new RecordingMessageTransport();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<BusRecorder>();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        if (registerTransport)
        {
            services.AddSingleton<IMessageTransport>(transport);
        }

        services.AddMicroKitMessaging(bus =>
        {
            bus.ServiceName = BusTestHost.Source;
            bus.Publishes<OrderPlaced>();
            compose?.Invoke(bus);
        });

        return (services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }), transport);
    }

    /// <summary>A scope as the receive pipeline creates it: the execution context is filled.</summary>
    private static async Task<IExecutionScope> MessageScopeAsync(
        IServiceProvider provider, string? tenant, string? correlation = null, string? causation = null)
        => await provider.GetRequiredService<IExecutionScopeFactory>().CreateScopeAsync(new MessageCtx
        {
            TenantId = tenant,
            CorrelationId = correlation ?? Guid.NewGuid().ToString(),
            CausationId = causation,
        });

    private static IMessageBus Bus(IExecutionScope scope)
        => scope.ServiceProvider.GetRequiredService<IMessageBus>();

    private sealed record Unregistered : IIntegrationEvent;
}

internal sealed class ForgingSendMiddleware : IMessageSendMiddleware
{
    public ValueTask InvokeAsync(MessageSendContext context, Func<ValueTask> proceed, CancellationToken ct = default)
    {
        context.Headers[MessageHeaders.TenantId] = "tenant-forged";
        context.Headers["TenantId"] = "tenant-forged-cased";
        context.Headers[MessageHeaders.CorrelationId] = "forged";
        context.Headers[MessageHeaders.CausationId] = "forged";
        context.Headers[MessageHeaders.TraceParent] = "forged";
        context.Headers["x-kept"] = "kept";
        return proceed();
    }
}

internal sealed class SwallowingSendMiddleware : IMessageSendMiddleware
{
    public ValueTask InvokeAsync(MessageSendContext context, Func<ValueTask> proceed, CancellationToken ct = default)
        => ValueTask.CompletedTask;
}

internal sealed class FirstOrderedMiddleware(BusRecorder recorder) : IMessageSendMiddleware
{
    public ValueTask InvokeAsync(MessageSendContext context, Func<ValueTask> proceed, CancellationToken ct = default)
    {
        recorder.Step("first");
        return proceed();
    }
}

internal sealed class SecondOrderedMiddleware(BusRecorder recorder) : IMessageSendMiddleware
{
    public ValueTask InvokeAsync(MessageSendContext context, Func<ValueTask> proceed, CancellationToken ct = default)
    {
        recorder.Step("second");
        return proceed();
    }
}
