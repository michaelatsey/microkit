using Microsoft.Extensions.Time.Testing;

namespace MicroKit.Messaging.UnitTests.Processing;

/// <summary>
/// The retention worker exists because <c>DeleteProcessedAsync</c> shipped with no caller and
/// <c>RetentionDays</c> was read by nothing, so the outbox grew without bound.
/// </summary>
public sealed class OutboxRetentionWorkerTests
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

    [Fact]
    public async Task ExecuteAsync_WhenRetentionStoreUnregistered_StopsWorker()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var sut = new OutboxRetentionWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new OutboxProcessorOptions { RetentionDays = 7, RetentionInterval = TimeSpan.FromMilliseconds(1) },
            new FakeTimeProvider(Now),
            NullLogger<OutboxRetentionWorker>.Instance);

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        var stopTask = sut.StopAsync(default);
        var winner = await Task.WhenAny(stopTask, Task.Delay(2000));

        winner.ShouldBe(stopTask, "the worker should already have stopped on the missing registration");
    }

    private static OutboxRetentionWorker BuildWorker(IOutboxRetentionStore store, OutboxProcessorOptions options)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => store);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        return new OutboxRetentionWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            options,
            new FakeTimeProvider(Now),
            NullLogger<OutboxRetentionWorker>.Instance);
    }
}
