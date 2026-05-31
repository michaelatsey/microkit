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
/// Verifies that the real MediatR engine dispatches to MicroKit command and query handlers
/// end-to-end, with LoggingBehavior active and the typed extension methods working correctly.
/// </summary>
public sealed class FullPipelineTests
{
    private static ServiceProvider BuildPipeline()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(new DomainEventLog());
        services.AddMicroKitMediatR(cfg => cfg
            .FromAssemblyContaining<EchoCommand>()
            .AddLoggingBehavior());
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task SendCommandAsync_WhenCommandIsValid_ReturnsHandlerResult()
    {
        using var provider = BuildPipeline();
        var mediator = provider.GetRequiredService<IMediator>();

        var result = await mediator.SendCommandAsync<EchoCommand, Result<string>>(
            new EchoCommand("hello"));

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("hello");
    }

    [Fact]
    public async Task SendQueryAsync_WhenQueryIsValid_ReturnsHandlerResult()
    {
        using var provider = BuildPipeline();
        var mediator = provider.GetRequiredService<IMediator>();

        var result = await mediator.SendQueryAsync<DoubleQuery, Result<int>>(
            new DoubleQuery(21));

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public async Task Handle_WhenCancellationTokenPreCancelled_HandlerThrowsOperationCancelledException()
    {
        using var provider = BuildPipeline();
        var mediator = provider.GetRequiredService<IMediator>();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await mediator.SendCommandAsync<CancellableCommand, Result<string>>(
                new CancellableCommand(), cts.Token));
    }

    [Fact]
    public async Task LoggingBehavior_WhenCommandDispatched_DoesNotModifyResponse()
    {
        // Logging is always active — verify it never alters the handler's return value.
        using var provider = BuildPipeline();
        var mediator = provider.GetRequiredService<IMediator>();

        var result = await mediator.SendCommandAsync<EchoCommand, Result<string>>(
            new EchoCommand("untouched"));

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("untouched");
    }
}
