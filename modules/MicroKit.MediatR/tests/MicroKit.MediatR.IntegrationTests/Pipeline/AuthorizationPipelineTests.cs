using System.Security.Claims;
using MediatR;
using MicroKit.MediatR;
using MicroKit.MediatR.Behaviors.DependencyInjection;
using MicroKit.MediatR.Behaviors.Errors;
using MicroKit.MediatR.IntegrationTests.Fixtures;
using MicroKit.Result;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace MicroKit.MediatR.IntegrationTests.Pipeline;

/// <summary>
/// Verifies AuthorizationBehavior integration with the real MediatR pipeline.
/// Marker absent → transparent pass-through.
/// Marker present, policy passes → handler invoked.
/// Marker present, policy fails → <c>UnauthorizedError</c> returned.
/// Marker present, no user → <c>UnauthenticatedError</c> returned.
/// </summary>
public sealed class AuthorizationPipelineTests
{
    private static ServiceProvider BuildPipeline(
        IAuthorizationService authService,
        ICurrentUserAccessor userAccessor)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(new DomainEventLog());
        services.AddSingleton(authService);
        services.AddSingleton(userAccessor);
        services.AddMicroKitMediatR(cfg => cfg
            .FromAssemblyContaining<SecureCommand>()
            .AddLoggingBehavior()
            .AddAuthorizationBehavior());
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Handle_WhenAllPoliciesPass_HandlerIsInvoked()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user1")]));
        using var provider = BuildPipeline(
            authService: new AlwaysSucceedAuthService(),
            userAccessor: new FixedUserAccessor(principal));

        var mediator = provider.GetRequiredService<IMediator>();

        var result = await mediator.SendCommandAsync<SecureCommand, Result<string>>(
            new SecureCommand());

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("authorized");
    }

    [Fact]
    public async Task Handle_WhenPolicyFails_ReturnsUnauthorizedError()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user1")]));
        using var provider = BuildPipeline(
            authService: new AlwaysFailAuthService(),
            userAccessor: new FixedUserAccessor(principal));

        var mediator = provider.GetRequiredService<IMediator>();

        var result = await mediator.SendCommandAsync<SecureCommand, Result<string>>(
            new SecureCommand());

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<UnauthorizedError>();
        var error = (UnauthorizedError)result.Error;
        error.PolicyName.ShouldBe("Admin");
    }

    [Fact]
    public async Task Handle_WhenUserIsNull_ReturnsUnauthenticatedError()
    {
        using var provider = BuildPipeline(
            authService: new AlwaysSucceedAuthService(),
            userAccessor: new FixedUserAccessor(current: null));

        var mediator = provider.GetRequiredService<IMediator>();

        var result = await mediator.SendCommandAsync<SecureCommand, Result<string>>(
            new SecureCommand());

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<UnauthenticatedError>();
    }

    [Fact]
    public async Task Handle_WhenRequestDoesNotImplementIAuthorizedRequest_PassesThrough()
    {
        // EchoCommand does not implement IAuthorizedRequest — AuthorizationBehavior must pass through.
        using var provider = BuildPipeline(
            authService: new AlwaysFailAuthService(),  // would fail if consulted
            userAccessor: new FixedUserAccessor(current: null)); // would fail if consulted

        var mediator = provider.GetRequiredService<IMediator>();

        var result = await mediator.SendCommandAsync<EchoCommand, Result<string>>(
            new EchoCommand("passthrough"));

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("passthrough");
    }

    // ── Test stubs ─────────────────────────────────────────────────────────

    private sealed class FixedUserAccessor(ClaimsPrincipal? current) : ICurrentUserAccessor
    {
        public ClaimsPrincipal? Current => current;
    }

    private sealed class AlwaysSucceedAuthService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Success());

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(AuthorizationResult.Success());
    }

    private sealed class AlwaysFailAuthService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(AuthorizationResult.Failed());
    }
}
