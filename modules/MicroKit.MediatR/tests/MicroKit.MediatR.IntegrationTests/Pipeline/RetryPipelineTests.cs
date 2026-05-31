using MediatR;
using MicroKit.MediatR;
using MicroKit.MediatR.Behaviors.DependencyInjection;
using MicroKit.MediatR.IntegrationTests.Fixtures;
using MicroKit.Result;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace MicroKit.MediatR.IntegrationTests.Pipeline;

/// <summary>
/// Verifies RetryBehavior integration with the real MediatR pipeline.
/// Transient exceptions are retried; non-retriable exceptions (OperationCancelledException)
/// propagate immediately; exhausted retries rethrow the last exception.
/// </summary>
public sealed class RetryPipelineTests
{
    private static ServiceProvider BuildPipeline(AttemptCounter counter)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(new DomainEventLog());
        services.AddSingleton(counter);
        services.AddMicroKitMediatR(cfg => cfg
            .FromAssemblyContaining<EchoCommand>()
            .AddLoggingBehavior()
            .AddRetryBehavior());
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Handle_WhenTransientException_RetriesAndEventuallySucceeds()
    {
        var counter = new AttemptCounter();
        using var provider = BuildPipeline(counter);
        var mediator = provider.GetRequiredService<IMediator>();

        // RetrySucceedAfterTwoHandler throws IOException twice then succeeds on attempt 3.
        var result = await mediator.SendCommandAsync<RetrySucceedAfterTwoCommand, Result<string>>(
            new RetrySucceedAfterTwoCommand());

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("after retries");
        counter.Count.ShouldBe(3, "handler must be called exactly 3 times (2 failures + 1 success)");
    }

    [Fact]
    public async Task Handle_WhenAllRetriesExhausted_ThrowsLastException()
    {
        var counter = new AttemptCounter();
        using var provider = BuildPipeline(counter);
        var mediator = provider.GetRequiredService<IMediator>();

        // RetryAlwaysFailHandler always throws IOException. MaxRetries=2 → 1 initial + 2 retries = 3 calls.
        var ex = await Should.ThrowAsync<IOException>(
            async () => await mediator.SendCommandAsync<RetryAlwaysFailCommand, Result<string>>(
                new RetryAlwaysFailCommand()));

        ex.Message.ShouldBe("always fails");
        counter.Count.ShouldBe(3, "handler must be called exactly 3 times (1 initial + 2 retries) before Polly gives up");
    }

    [Fact]
    public async Task Handle_WhenCancellationTokenCancelled_DoesNotRetry()
    {
        // OperationCanceledException is excluded from retry — it must propagate immediately.
        // We test this through the CancellableHandler which throws when CT is pre-cancelled.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var counter = new AttemptCounter();
        using var provider = BuildPipeline(counter);
        var mediator = provider.GetRequiredService<IMediator>();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await mediator.SendCommandAsync<CancellableCommand, Result<string>>(
                new CancellableCommand(), cts.Token));
    }
}
