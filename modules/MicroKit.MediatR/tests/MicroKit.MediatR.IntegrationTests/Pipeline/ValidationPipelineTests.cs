using FluentValidation;
using MediatR;
using MicroKit.MediatR;
using MicroKit.MediatR.Behaviors.DependencyInjection;
using MicroKit.MediatR.Behaviors.Errors;
using MicroKit.MediatR.IntegrationTests.Fixtures;
using MicroKit.Result;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using MicroKitValidationError = MicroKit.MediatR.Behaviors.Errors.ValidationError;
using Xunit;

namespace MicroKit.MediatR.IntegrationTests.Pipeline;

/// <summary>
/// Verifies the ValidationBehavior integration with the real MediatR pipeline.
/// No validator registered → transparent pass-through.
/// Validator registered + valid input → handler invoked.
/// Validator registered + invalid input → handler NOT invoked, <c>ValidationError</c> returned.
/// </summary>
public sealed class ValidationPipelineTests
{
    private static ServiceProvider BuildPipeline(bool withValidator)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(new DomainEventLog());
        services.AddMicroKitMediatR(cfg => cfg
            .FromAssemblyContaining<ValidatableCommand>()
            .AddLoggingBehavior()
            .AddValidationBehavior());

        if (withValidator)
            services.AddScoped<IValidator<ValidatableCommand>, ValidatableCommandValidator>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Handle_WhenNoValidatorRegistered_PassesThrough()
    {
        using var provider = BuildPipeline(withValidator: false);
        var mediator = provider.GetRequiredService<IMediator>();

        var result = await mediator.SendCommandAsync<ValidatableCommand, Result<int>>(
            new ValidatableCommand(5));

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(10);
    }

    [Fact]
    public async Task Handle_WhenValidationPasses_HandlerIsInvoked()
    {
        using var provider = BuildPipeline(withValidator: true);
        var mediator = provider.GetRequiredService<IMediator>();

        var result = await mediator.SendCommandAsync<ValidatableCommand, Result<int>>(
            new ValidatableCommand(7));

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(14);
    }

    [Fact]
    public async Task Handle_WhenValidationFails_ReturnsValidationErrorWithoutCallingHandler()
    {
        using var provider = BuildPipeline(withValidator: true);
        var mediator = provider.GetRequiredService<IMediator>();

        // Value = -1 fails the GreaterThan(0) rule.
        var result = await mediator.SendCommandAsync<ValidatableCommand, Result<int>>(
            new ValidatableCommand(-1));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<MicroKitValidationError>();
        var error = (MicroKitValidationError)result.Error;
        error.Failures.ShouldNotBeEmpty();
        error.Failures.ShouldContain(f => f.PropertyName == "Value");
    }
}
