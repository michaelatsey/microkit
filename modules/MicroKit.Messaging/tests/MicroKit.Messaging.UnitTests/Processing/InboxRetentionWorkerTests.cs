using Microsoft.Extensions.Time.Testing;

namespace MicroKit.Messaging.UnitTests.Processing;

public sealed class InboxRetentionWorkerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    private static InboxProcessorOptions FastOptions => new()
    {
        RetentionDays = 30,
        RetentionInterval = TimeSpan.FromMilliseconds(10),
    };

    [Fact]
    public async Task ExecuteAsync_DeletesProcessedRowsOlderThanTheRetentionWindow()
    {
        var store = Substitute.For<IInboxRetentionStore>();
        store.DeleteProcessedAsync(
                Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(3));

        var sut = BuildWorker(store, FastOptions);

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        await sut.StopAsync(default);

        await store.Received().DeleteProcessedAsync(
            Now.AddDays(-30), null, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The window is deliberately not the outbox's. Deleting an inbox row before its message can
    /// still be redelivered reopens reprocessing, where on the outbox it only loses history.
    /// </summary>
    [Fact]
    public void DefaultRetentionWindow_IsLongerThanTheOutboxs()
    {
        var inbox = new InboxProcessorOptions();
        var outbox = new OutboxProcessorOptions();

        inbox.RetentionDays.ShouldBe(30);
        inbox.RetentionDays.ShouldBeGreaterThan(
            outbox.RetentionDays,
            "the inbox window must exceed the maximum plausible redelivery delay, not merely " +
            "bound table growth");
    }

    [Fact]
    public async Task ExecuteAsync_DeletesAcrossEveryTenant()
    {
        // tenantId null is also the only value that reaches rows in a single-tenant deployment,
        // where TenantId is itself null.
        var store = Substitute.For<IInboxRetentionStore>();
        store.DeleteProcessedAsync(
                Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(0));

        var sut = BuildWorker(store, FastOptions);

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await sut.StopAsync(default);

        await store.Received().DeleteProcessedAsync(
            Arg.Any<DateTimeOffset>(), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenRetentionDisabled_NeverDeletes()
    {
        var store = Substitute.For<IInboxRetentionStore>();
        var options = FastOptions;
        options.RetentionDays = 0;

        var sut = BuildWorker(store, options);

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await sut.StopAsync(default);

        await store.DidNotReceive().DeleteProcessedAsync(
            Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenStoreUnresolvable_StopsWithoutThrowing()
    {
        var services = new ServiceCollection();
        services.AddLogging(); // no IInboxRetentionStore
        var provider = services.BuildServiceProvider();

        var sut = new InboxRetentionWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            FastOptions,
            new FakeTimeProvider(Now),
            NullLogger<InboxRetentionWorker>.Instance);

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        var stopTask = sut.StopAsync(default);
        (await Task.WhenAny(stopTask, Task.Delay(2000))).ShouldBe(stopTask);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAPassFails_KeepsRunning()
    {
        // Housekeeping must never take the host down: the next pass retries.
        var callCount = 0;
        var store = Substitute.For<IInboxRetentionStore>();
        store.DeleteProcessedAsync(
                Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref callCount);
                return ValueTask.FromException<int>(new InvalidOperationException("database gone"));
            });

        var sut = BuildWorker(store, FastOptions);

        await Should.NotThrowAsync(async () =>
        {
            await sut.StartAsync(CancellationToken.None);
            await Task.Delay(200);
            await sut.StopAsync(default);
        });

        callCount.ShouldBeGreaterThan(1);
    }

    private static InboxRetentionWorker BuildWorker(
        IInboxRetentionStore store, InboxProcessorOptions options)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => store);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        return new InboxRetentionWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            options,
            new FakeTimeProvider(Now),
            NullLogger<InboxRetentionWorker>.Instance);
    }
}
