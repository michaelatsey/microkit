using MicroKit.Persistence.Testing;

namespace MicroKit.Persistence.UnitTests.Testing;

/// <summary>
/// #197 phase 7 (ADR-015): the lifecycle the PostgreSQL and SQL Server fixtures share — one start under no
/// caller's token, a caller's token cancels only its wait, a failed or interrupted start cached, a dispose
/// that cancels and waits for the start and still removes the container. A fake container, no Docker.
/// </summary>
public sealed class ContainerLifecycleTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task StartAsync_ConcurrentCalls_BuildAndStartOneContainer()
    {
        var harness = new Harness();
        await using var lifecycle = harness.Create();

        var first = lifecycle.StartAsync(CancellationToken.None).AsTask();
        var second = lifecycle.StartAsync(CancellationToken.None).AsTask();
        await harness.StartBegun.Task.WaitAsync(Wait);
        harness.Release();
        await Task.WhenAll(first, second).WaitAsync(Wait);
        await lifecycle.StartAsync(CancellationToken.None).AsTask().WaitAsync(Wait);

        harness.Builds.ShouldBe(1);
        harness.Starts.ShouldBe(1);
        lifecycle.Running.ShouldBeSameAs(harness.Containers.Single());
    }

    [Fact]
    public async Task StartAsync_TheFirstCallersCancelledWait_LeavesTheStartRunningForTheOthers()
    {
        var harness = new Harness();
        await using var lifecycle = harness.Create();
        using var first = new CancellationTokenSource();

        var cancelled = lifecycle.StartAsync(first.Token).AsTask();
        await harness.StartBegun.Task.WaitAsync(Wait);
        var other = lifecycle.StartAsync(CancellationToken.None).AsTask();
        await first.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => cancelled.WaitAsync(Wait));
        harness.StartToken.IsCancellationRequested.ShouldBeFalse("the shared start runs under no caller's token");
        harness.Release();
        await other.WaitAsync(Wait);
        lifecycle.Running.ShouldBeSameAs(harness.Containers.Single());

        // The cancelled wait is not cached: a later call sees the successful start.
        await lifecycle.StartAsync(CancellationToken.None).AsTask().WaitAsync(Wait);
        harness.Starts.ShouldBe(1);
    }

    [Fact]
    public async Task StartAsync_WithAnAlreadyCancelledToken_ThrowsWithoutStarting()
    {
        var harness = new Harness();
        await using var lifecycle = harness.Create();

        await Should.ThrowAsync<OperationCanceledException>(() => lifecycle.StartAsync(new CancellationToken(canceled: true)).AsTask());

        harness.Builds.ShouldBe(0);
    }

    [Fact]
    public async Task StartAsync_AfterAFailedStart_RethrowsTheSameFailure_WithoutRetrying()
    {
        var failure = new InvalidOperationException("the image does not exist");
        var harness = new Harness { StartFailure = failure };
        await using var lifecycle = harness.Create();
        harness.Release();

        (await Should.ThrowAsync<InvalidOperationException>(() => lifecycle.StartAsync(CancellationToken.None).AsTask().WaitAsync(Wait)))
            .ShouldBeSameAs(failure);
        (await Should.ThrowAsync<InvalidOperationException>(() => lifecycle.StartAsync(CancellationToken.None).AsTask().WaitAsync(Wait)))
            .ShouldBeSameAs(failure);

        harness.Builds.ShouldBe(1);
        harness.Starts.ShouldBe(1);
        Should.Throw<InvalidOperationException>(() => lifecycle.Running).ShouldNotBeSameAs(failure);
    }

    [Fact]
    public async Task StartAsync_WhenTheBuildThrows_CachesTheFailure_AndDisposeHasNoContainer()
    {
        var failure = new ArgumentException("bad image reference");
        var harness = new Harness { BuildFailure = failure };
        var lifecycle = harness.Create();

        (await Should.ThrowAsync<ArgumentException>(() => lifecycle.StartAsync(CancellationToken.None).AsTask().WaitAsync(Wait)))
            .ShouldBeSameAs(failure);
        (await Should.ThrowAsync<ArgumentException>(() => lifecycle.StartAsync(CancellationToken.None).AsTask().WaitAsync(Wait)))
            .ShouldBeSameAs(failure);

        await lifecycle.DisposeAsync();
        harness.Containers.ShouldBeEmpty();
    }

    [Fact]
    public async Task DisposeAsync_DuringTheStart_CancelsIt_WaitsForIt_AndDisposesTheContainer()
    {
        var harness = new Harness();
        var lifecycle = harness.Create();
        var start = lifecycle.StartAsync(CancellationToken.None).AsTask();
        await harness.StartBegun.Task.WaitAsync(Wait);

        await lifecycle.DisposeAsync().AsTask().WaitAsync(Wait);

        harness.StartToken.IsCancellationRequested.ShouldBeTrue("a dispose interrupts the start");
        harness.StartEnded.ShouldBeTrue("the dispose waits for the start before removing the container");
        harness.Containers.Single().DisposeCount.ShouldBe(1);
        await Should.ThrowAsync<OperationCanceledException>(() => start.WaitAsync(Wait));
        await Should.ThrowAsync<ObjectDisposedException>(() => lifecycle.StartAsync(CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task DisposeAsync_DuringAStartSlowToWindDown_RemovesTheContainerOnlyOnceTheStartEnded()
    {
        var harness = new Harness { IgnoreCancellation = true };
        var lifecycle = harness.Create();
        _ = lifecycle.StartAsync(CancellationToken.None).AsTask();
        await harness.StartBegun.Task.WaitAsync(Wait);

        var dispose = lifecycle.DisposeAsync().AsTask();
        await Task.Delay(100);

        dispose.IsCompleted.ShouldBeFalse("the dispose waits for the start before removing the container");
        harness.Containers.Single().DisposeCount.ShouldBe(0);
        harness.Release();
        await dispose.WaitAsync(Wait);
        harness.Containers.Single().StartEndedAtDispose.ShouldBeTrue();
    }

    [Fact]
    public async Task DisposeAsync_AfterAFailedStart_StillDisposesTheBuiltContainer()
    {
        var harness = new Harness { StartFailure = new InvalidOperationException("the wait strategy gave up") };
        var lifecycle = harness.Create();
        harness.Release();
        await Should.ThrowAsync<InvalidOperationException>(() => lifecycle.StartAsync(CancellationToken.None).AsTask().WaitAsync(Wait));

        await lifecycle.DisposeAsync();

        harness.Containers.Single().DisposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task DisposeAsync_AfterAStart_DisposesTheContainerOnce_AndIsIdempotent()
    {
        var harness = new Harness();
        var lifecycle = harness.Create();
        harness.Release();
        await lifecycle.StartAsync(CancellationToken.None).AsTask().WaitAsync(Wait);

        await lifecycle.DisposeAsync();
        await lifecycle.DisposeAsync();

        harness.Containers.Single().DisposeCount.ShouldBe(1);
        Should.Throw<ObjectDisposedException>(() => lifecycle.Running).ObjectName.ShouldBe(typeof(TestFixture).FullName);
        (await Should.ThrowAsync<ObjectDisposedException>(() => lifecycle.StartAsync(CancellationToken.None).AsTask()))
            .ObjectName.ShouldBe(typeof(TestFixture).FullName);
    }

    [Fact]
    public async Task DisposeAsync_OnALifecycleThatNeverStarted_IsANoOp_Twice()
    {
        var harness = new Harness();
        var lifecycle = harness.Create();

        await Should.NotThrowAsync(async () => await lifecycle.DisposeAsync());
        await Should.NotThrowAsync(async () => await lifecycle.DisposeAsync());

        harness.Builds.ShouldBe(0);
        Should.Throw<ObjectDisposedException>(() => lifecycle.Running);
    }

    [Fact]
    public async Task Running_BeforeAndDuringTheStart_Throws_NamingStartAsyncAndTheProbe()
    {
        var harness = new Harness();
        await using var lifecycle = harness.Create();

        var before = Should.Throw<InvalidOperationException>(() => lifecycle.Running);
        var start = lifecycle.StartAsync(CancellationToken.None).AsTask();
        await harness.StartBegun.Task.WaitAsync(Wait);
        Should.Throw<InvalidOperationException>(() => lifecycle.Running);
        harness.Release();
        await start.WaitAsync(Wait);

        before.Message.ShouldContain("TestFixture");
        before.Message.ShouldContain("StartAsync");
        before.Message.ShouldContain("DockerProbe.IsAvailable");
        lifecycle.Running.ShouldNotBeNull();
    }

    [Fact]
    public async Task StartAsync_ReportsAFaultThroughTheReturnedTask_NeverAtTheCallSite()
    {
        var harness = new Harness();
        var lifecycle = harness.Create();

        var cancelled = Should.NotThrow(() => lifecycle.StartAsync(new CancellationToken(canceled: true)));
        await lifecycle.DisposeAsync();
        var disposed = Should.NotThrow(() => lifecycle.StartAsync(CancellationToken.None));

        await Should.ThrowAsync<OperationCanceledException>(() => cancelled.AsTask());
        await Should.ThrowAsync<ObjectDisposedException>(() => disposed.AsTask());
    }

    [Fact]
    public async Task StartAsync_TheSharedStart_DoesNotCarryTheFirstCallersAsyncLocals()
    {
        var harness = new Harness();
        await using var lifecycle = harness.Create();
        harness.Release();

        Ambient.Value = "first caller";
        await lifecycle.StartAsync(CancellationToken.None).AsTask().WaitAsync(Wait);
        Ambient.Value = null;

        harness.AmbientAtStart.ShouldBeNull("the start belongs to no caller: no caller's ambient state flows into it");
    }

    [Fact]
    public async Task DisposeAsync_ASecondConcurrentCall_CompletesOnlyOnceTheContainerIsRemoved()
    {
        var harness = new Harness { HoldContainerDispose = true };
        var lifecycle = harness.Create();
        harness.Release();
        await lifecycle.StartAsync(CancellationToken.None).AsTask().WaitAsync(Wait);

        var first = lifecycle.DisposeAsync().AsTask();
        var second = lifecycle.DisposeAsync().AsTask();
        await harness.ContainerDisposeBegun.Task.WaitAsync(Wait);
        await Task.Delay(100);

        second.IsCompleted.ShouldBeFalse("a dispose returns when the container is removed, whoever called first");
        harness.ReleaseContainerDispose();
        await Task.WhenAll(first, second).WaitAsync(Wait);
        harness.Containers.Single().DisposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task DisposeAsync_WhenACancellationCallbackOfTheStartThrows_StillRemovesTheContainer()
    {
        var harness = new Harness { ThrowOnCancel = true };
        var lifecycle = harness.Create();
        var start = lifecycle.StartAsync(CancellationToken.None).AsTask();
        await harness.StartBegun.Task.WaitAsync(Wait);

        await Should.NotThrowAsync(() => lifecycle.DisposeAsync().AsTask().WaitAsync(Wait));

        harness.Containers.Single().DisposeCount.ShouldBe(1);
        await Should.ThrowAsync<Exception>(() => start.WaitAsync(Wait));
    }

    [Fact]
    public void WithDatabase_ReplacesTheDatabase_AndDropsTheInitialCatalogSynonym()
    {
        var edited = ConnectionStrings.WithDatabase("Server=db,1433;Initial Catalog=master;User Id=sa;Password=p", "orders");

        var parsed = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = edited };
        parsed["Database"].ShouldBe("orders");
        parsed.ContainsKey("Initial Catalog").ShouldBeFalse();
        parsed["Server"].ShouldBe("db,1433");
        parsed["Password"].ShouldBe("p");
    }

    [Theory]
    [InlineData("we;ird")]
    [InlineData("a=b")]
    [InlineData("it's")]
    [InlineData("\"quoted\"")]
    public void WithDatabase_QuotesANameTheParserReadsBack(string database)
    {
        var edited = ConnectionStrings.WithDatabase("Host=h;Database=postgres;Username=u", database);

        new System.Data.Common.DbConnectionStringBuilder { ConnectionString = edited }["Database"].ShouldBe(database);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void WithDatabase_ABlankName_Throws(string? database) =>
        Should.Throw<ArgumentException>(() => ConnectionStrings.WithDatabase("Host=h", database!));

    private static readonly AsyncLocal<string?> Ambient = new();

    /// <summary>Builds fake containers and holds their start until <see cref="Release"/>.</summary>
    private sealed class Harness
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseContainerDispose = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ContainerDisposeBegun { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool HoldContainerDispose { get; init; }

        public bool ThrowOnCancel { get; init; }

        public bool IgnoreCancellation { get; init; }

        public string? AmbientAtStart { get; private set; }

        public void ReleaseContainerDispose() => _releaseContainerDispose.TrySetResult();

        public Task WaitContainerDisposeReleaseAsync() => _releaseContainerDispose.Task;

        public TaskCompletionSource StartBegun { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<FakeContainer> Containers { get; } = [];

        public int Builds { get; private set; }

        public int Starts { get; private set; }

        public bool StartEnded { get; private set; }

        public CancellationToken StartToken { get; private set; }

        public Exception? BuildFailure { get; init; }

        public Exception? StartFailure { get; init; }

        public ContainerLifecycle<FakeContainer> Create() => new(typeof(TestFixture), Build, StartAsync);

        public void Release() => _release.TrySetResult();

        private FakeContainer Build()
        {
            Builds++;
            if (BuildFailure is not null)
                throw BuildFailure;
            var container = new FakeContainer(this);
            Containers.Add(container);
            return container;
        }

        private async Task StartAsync(FakeContainer container, CancellationToken ct)
        {
            Starts++;
            StartToken = ct;
            AmbientAtStart = Ambient.Value;
            // Never unregistered: it must still be there when the dispose cancels the start.
            if (ThrowOnCancel)
                ct.Register(static () => throw new InvalidOperationException("a callback failed"));
            StartBegun.TrySetResult();
            try
            {
                await (IgnoreCancellation ? _release.Task : _release.Task.WaitAsync(ct));
                if (StartFailure is not null)
                    throw StartFailure;
            }
            finally
            {
                StartEnded = true;
            }
        }
    }

    /// <summary>Stands for the fixture that owns the lifecycle: the name its exceptions carry.</summary>
    private sealed class TestFixture;

    private sealed class FakeContainer(Harness harness) : IAsyncDisposable
    {
        public int DisposeCount { get; private set; }

        public bool StartEndedAtDispose { get; private set; }

        public async ValueTask DisposeAsync()
        {
            DisposeCount++;
            StartEndedAtDispose = harness.StartEnded;
            harness.ContainerDisposeBegun.TrySetResult();
            if (harness.HoldContainerDispose)
                await harness.WaitContainerDisposeReleaseAsync();
        }
    }
}
