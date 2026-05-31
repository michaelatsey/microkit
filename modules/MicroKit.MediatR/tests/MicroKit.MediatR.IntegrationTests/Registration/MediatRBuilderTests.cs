using MediatR;
using MicroKit.MediatR;
using MicroKit.MediatR.Behaviors;
using MicroKit.MediatR.Behaviors.DependencyInjection;
using MicroKit.MediatR.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace MicroKit.MediatR.IntegrationTests.Registration;

/// <summary>
/// Verifies that <see cref="MediatRBuilder"/> rejects invalid behavior registrations
/// and duplicate assembly scans at DI setup time, per the rules documented in ADR-002.
/// </summary>
public sealed class MediatRBuilderTests
{
    [Fact]
    public void AddOpenBehavior_WhenTypeDoesNotInheritBehaviorBase_ThrowsArgumentException()
    {
        var services = new ServiceCollection();

        var ex = Should.Throw<ArgumentException>(() =>
            services.AddMicroKitMediatR(cfg =>
                cfg.AddOpenBehavior(typeof(DirectPipelineBehavior<,>))));

        ex.Message.ShouldContain("BehaviorBase");
    }

    [Fact]
    public void AddOpenBehavior_WhenTypeDoesNotImplementIPipelineBehavior_ThrowsArgumentException()
    {
        var services = new ServiceCollection();

        var ex = Should.Throw<ArgumentException>(() =>
            services.AddMicroKitMediatR(cfg =>
                cfg.AddOpenBehavior(typeof(NotAPipelineBehavior<,>))));

        ex.Message.ShouldContain("IPipelineBehavior");
    }

    [Fact]
    public void AddOpenBehavior_WhenTypeIsClosedGeneric_ThrowsArgumentException()
    {
        // A closed type (e.g. LoggingBehavior<EchoCommand, Result<string>>) is not an open generic definition.
        var services = new ServiceCollection();

        var ex = Should.Throw<ArgumentException>(() =>
            services.AddMicroKitMediatR(cfg =>
                cfg.AddOpenBehavior(typeof(LoggingBehavior<EchoCommand, MicroKit.Result.Result<string>>))));

        ex.Message.ShouldContain("open generic");
    }

    [Fact]
    public void AddOpenBehavior_WhenSameBehaviorRegisteredTwice_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();

        var ex = Should.Throw<InvalidOperationException>(() =>
            services.AddMicroKitMediatR(cfg =>
            {
                cfg.AddLoggingBehavior();
                cfg.AddLoggingBehavior(); // duplicate
            }));

        ex.Message.ShouldContain("already registered");
    }

    [Fact]
    public void FromAssemblyContaining_WhenSameAssemblyRegisteredTwice_ThrowsArgumentException()
    {
        var services = new ServiceCollection();

        var ex = Should.Throw<ArgumentException>(() =>
            services.AddMicroKitMediatR(cfg =>
            {
                cfg.FromAssemblyContaining<EchoCommand>();
                cfg.FromAssemblyContaining<EchoCommand>(); // same assembly
            }));

        ex.Message.ShouldContain("already registered");
    }

    // ── Private invalid types for testing ─────────────────────────────────

    // Implements IPipelineBehavior directly — violates ADR-002 (no BehaviorBase inheritance).
    private sealed class DirectPipelineBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
            => next();
    }

    // Open generic type that does NOT implement IPipelineBehavior<,> at all.
    private sealed class NotAPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
    }
}
