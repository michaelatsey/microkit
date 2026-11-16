using Microsoft.Extensions.Time.Testing;

namespace MicroKit.Messaging.UnitTests.Processing;

/// <summary>
/// The retention worker exists because <c>DeleteProcessedAsync</c> shipped with no caller and
/// <c>RetentionDays</c> was read by nothing, so the outbox grew without bound.
/// </summary>
public sealed class OutboxRetentionLoopTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_DeletesPublishedRowsOlderThanTheRetentionWindow()
    {
        DateTimeOffset? cutoff = null;
        var store = Substitute.For<IOutboxRetentionStore>();
        store.DeleteProcessedAsync(Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                cutoff = ci.Arg<DateTimeOffset>();
                return ValueTask.FromResult(3);
            });

        var sut = BuildWorker(store, new OutboxProcessorOptions
        {
            RetentionDays = 7,
            RetentionInterval = TimeSpan.FromMilliseconds(10),
        });

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        await sut.StopAsync(default);

        cutoff.ShouldBe(Now.AddDays(-7));
    }

    [Fact]
    public async Task ExecuteAsync_DeletesAcrossEveryTenant()
    {
        var store = Substitute.For<IOutboxRetentionStore>();
        store.DeleteProcessedAsync(Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(0));

        var sut = BuildWorker(store, new OutboxProcessorOptions
        {
            RetentionDays = 7,
            RetentionInterval = TimeSpan.FromMilliseconds(10),
        });

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        await sut.StopAsync(default);

        // null tenant = every tenant, and the only value that reaches rows in a single-tenant
        // deployment where TenantId is itself null. The old non-nullable signature matched nothing.
        await store.Received().DeleteProcessedAsync(
            Arg.Any<DateTimeOffset>(), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenRetentionDisabled_NeverTouchesTheStore()
    {
        var store = Substitute.For<IOutboxRetentionStore>();

        var sut = BuildWorker(store, new OutboxProcessorOptions
        {
            RetentionDays = 0,
            RetentionInterval = TimeSpan.FromMilliseconds(10),
        });

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await sut.StopAsync(default);

        await store.DidNotReceive().DeleteProcessedAsync(
            Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenStoreThrows_KeepsRunning()
    {
        var callCount = 0;
        var store = Substitute.For<IOutboxRetentionStore>();
        store.DeleteProcessedAsync(Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                System.Threading.Interlocked.Increment(ref callCount);
                return ValueTask.FromException<int>(new InvalidOperationException("db down"));
            });

        var sut = BuildWorker(store, new OutboxProcessorOptions
        {
            RetentionDays = 7,
            RetentionInterval = TimeSpan.FromMilliseconds(10),
        });

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(200);
        await sut.StopAsync(default);

        // Housekeeping must never take the host down.
        callCount.ShouldBeGreaterThan(1);
    }

    /// <summary>
    /// A registered retention store whose construction throws is an activation failure (D11,
    /// #127): logged under its own event, and the loop keeps running.
    /// </summary>
    [Fact]
    public async Task RunAsync_WhenTheRetentionStoreCannotBeActivated_LogsAnActivationFailureAndKeepsRunning()
    {
        var attempts = 0;
        var store = Substitute.For<IOutboxRetentionStore>();
        var services = new ServiceCollection();
        services.AddKeyedScoped<IOutboxRetentionStore>(LoopHarness.Store, (_, _) =>
            Interlocked.Increment(ref attempts) == 1
                ? throw new InvalidOperationException("The store's constructor threw.")
                : store);
        var provider = services.BuildServiceProvider();
        var logger = new CapturingLogger<OutboxRetentionLoop>();

        var sut = new LoopHarness(new OutboxRetentionLoop(
            provider.GetRequiredService<IServiceScopeFactory>(),
            LoopHarness.Store,
            new OutboxProcessorOptions { RetentionDays = 7, RetentionInterval = TimeSpan.FromMilliseconds(5) },
            new FakeTimeProvider(Now),
            logger).RunAsync);

        await sut.StartAsync(CancellationToken.None);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!store.ReceivedCalls().Any() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        await sut.StopAsync(default);

        logger.Records.Count(r => r.EventId == 1400).ShouldBe(1, "an activation failure has its own event");
        await store.Received().DeleteProcessedAsync(
            Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    private static LoopHarness BuildWorker(IOutboxRetentionStore store, OutboxProcessorOptions options)
    {
        var services = new ServiceCollection();
        services.AddKeyedScoped<IOutboxRetentionStore>(LoopHarness.Store, (_, _) => store);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        return new LoopHarness(new OutboxRetentionLoop(
            provider.GetRequiredService<IServiceScopeFactory>(),
            LoopHarness.Store,
            options,
            new FakeTimeProvider(Now),
            NullLogger<OutboxRetentionLoop>.Instance).RunAsync);
    }
}
