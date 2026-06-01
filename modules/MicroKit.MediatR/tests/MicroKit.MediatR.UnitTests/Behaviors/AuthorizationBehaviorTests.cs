using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using MicroKit.MediatR;
using MicroKit.MediatR.Behaviors;
using MicroKit.Result;
using static MicroKit.Result.Result;
using NSubstitute;
using Shouldly;
using Xunit;
using MicroKit.MediatR.Behaviors.Errors;
using MicroKit.MediatR.Behaviors.Pipeline;

namespace MicroKit.MediatR.UnitTests.Behaviors;

public sealed class AuthorizationBehaviorTests
{
    [Fact]
    public async Task Handle_WhenMarkerAbsent_PassesThrough()
    {
        var callCount = 0;
        RequestHandlerDelegate<string> next = () => { callCount++; return Task.FromResult("result"); };
        var authService = Substitute.For<IAuthorizationService>();
        var userAccessor = Substitute.For<ICurrentUserAccessor>();
        var behavior = new AuthorizationBehavior<NonAuthorizedRequest, string>(authService, userAccessor);

        var result = await behavior.Handle(new NonAuthorizedRequest(), next, CancellationToken.None);

        result.ShouldBe("result");
        callCount.ShouldBe(1);
        await authService.DidNotReceive()
            .AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_WhenCurrentUserIsNull_ReturnsFailureForResultResponse()
    {
        var callCount = 0;
        RequestHandlerDelegate<Result<string>> next = () =>
            { callCount++; return Task.FromResult(Success("result")); };
        var authService = Substitute.For<IAuthorizationService>();
        var userAccessor = Substitute.For<ICurrentUserAccessor>();
        userAccessor.Current.Returns((ClaimsPrincipal?)null);
        var behavior = new AuthorizationBehavior<AuthorizedResultRequest, Result<string>>(authService, userAccessor);

        var result = await behavior.Handle(new AuthorizedResultRequest(), next, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<UnauthenticatedError>();
        callCount.ShouldBe(0);
    }

    [Fact]
    public async Task Handle_WhenCurrentUserIsNull_ThrowsForDirectResponse()
    {
        RequestHandlerDelegate<string> next = () => Task.FromResult("result");
        var authService = Substitute.For<IAuthorizationService>();
        var userAccessor = Substitute.For<ICurrentUserAccessor>();
        userAccessor.Current.Returns((ClaimsPrincipal?)null);
        var behavior = new AuthorizationBehavior<AuthorizedDirectRequest, string>(authService, userAccessor);

        var ex = await Should.ThrowAsync<UnauthorizedAccessException>(
            () => behavior.Handle(new AuthorizedDirectRequest(), next, CancellationToken.None));
        ex.Message.ShouldContain("No authenticated user context is available.");
    }

    [Fact]
    public async Task Handle_WhenAllPoliciesPass_CallsNext()
    {
        var callCount = 0;
        RequestHandlerDelegate<string> next = () => { callCount++; return Task.FromResult("result"); };
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user1")]));
        var authService = Substitute.For<IAuthorizationService>();
        authService.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(), "Admin")
            .Returns(Task.FromResult(AuthorizationResult.Success()));
        var userAccessor = Substitute.For<ICurrentUserAccessor>();
        userAccessor.Current.Returns(principal);
        var behavior = new AuthorizationBehavior<AuthorizedSinglePolicyRequest, string>(authService, userAccessor);

        var result = await behavior.Handle(new AuthorizedSinglePolicyRequest(), next, CancellationToken.None);

        result.ShouldBe("result");
        callCount.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_WhenPolicyFails_ShortCircuitsWithFailureResultResponse()
    {
        var callCount = 0;
        RequestHandlerDelegate<Result<string>> next = () =>
            { callCount++; return Task.FromResult(Success("result")); };
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user1")]));
        var authService = Substitute.For<IAuthorizationService>();
        authService.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(), "Admin")
            .Returns(Task.FromResult(AuthorizationResult.Failed()));
        var userAccessor = Substitute.For<ICurrentUserAccessor>();
        userAccessor.Current.Returns(principal);
        var behavior = new AuthorizationBehavior<AuthorizedResultFailureRequest, Result<string>>(authService, userAccessor);

        var result = await behavior.Handle(new AuthorizedResultFailureRequest(), next, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        callCount.ShouldBe(0);
    }

    [Fact]
    public async Task Handle_WhenPolicyFails_ThrowsForDirectResponse()
    {
        RequestHandlerDelegate<string> next = () => Task.FromResult("result");
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user1")]));
        var authService = Substitute.For<IAuthorizationService>();
        authService.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(), "Admin")
            .Returns(Task.FromResult(AuthorizationResult.Failed()));
        var userAccessor = Substitute.For<ICurrentUserAccessor>();
        userAccessor.Current.Returns(principal);
        var behavior = new AuthorizationBehavior<AuthorizedDirectFailureRequest, string>(authService, userAccessor);

        await Should.ThrowAsync<UnauthorizedAccessException>(
            () => behavior.Handle(new AuthorizedDirectFailureRequest(), next, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenFirstPolicyFails_DoesNotEvaluateRemainingPolicies()
    {
        var callCount = 0;
        RequestHandlerDelegate<Result<string>> next = () =>
            { callCount++; return Task.FromResult(Success("result")); };
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user1")]));
        var authService = Substitute.For<IAuthorizationService>();
        authService.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(), Arg.Is<string>(p => p == "Admin"))
            .Returns(Task.FromResult(AuthorizationResult.Failed()));
        authService.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(), Arg.Is<string>(p => p == "Editor"))
            .Returns(Task.FromResult(AuthorizationResult.Success()));
        var userAccessor = Substitute.For<ICurrentUserAccessor>();
        userAccessor.Current.Returns(principal);
        var behavior = new AuthorizationBehavior<AuthorizedMultiplePoliciesRequest, Result<string>>(authService, userAccessor);

        var result = await behavior.Handle(new AuthorizedMultiplePoliciesRequest(), next, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        callCount.ShouldBe(0);
        await authService.Received(1).AuthorizeAsync(
            Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(), Arg.Is<string>(p => p == "Admin"));
        await authService.DidNotReceive().AuthorizeAsync(
            Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(), Arg.Is<string>(p => p == "Editor"));
    }

    [Fact]
    public async Task Handle_WhenRequiredPoliciesIsEmpty_CallsNext()
    {
        var callCount = 0;
        RequestHandlerDelegate<string> next = () => { callCount++; return Task.FromResult("result"); };
        var authService = Substitute.For<IAuthorizationService>();
        var userAccessor = Substitute.For<ICurrentUserAccessor>();
        userAccessor.Current.Returns(new ClaimsPrincipal(new ClaimsIdentity()));
        var behavior = new AuthorizationBehavior<EmptyPoliciesRequest, string>(authService, userAccessor);

        var result = await behavior.Handle(new EmptyPoliciesRequest(), next, CancellationToken.None);

        result.ShouldBe("result");
        callCount.ShouldBe(1);
        await authService.DidNotReceive()
            .AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_WhenPolicyFails_ErrorCarriesPolicyName()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user1")]));
        var authService = Substitute.For<IAuthorizationService>();
        authService.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(), "Admin")
            .Returns(Task.FromResult(AuthorizationResult.Failed()));
        var userAccessor = Substitute.For<ICurrentUserAccessor>();
        userAccessor.Current.Returns(principal);
        var behavior = new AuthorizationBehavior<AuthorizedResultPolicyNameRequest, Result<string>>(authService, userAccessor);

        var result = await behavior.Handle(new AuthorizedResultPolicyNameRequest(), () => Task.FromResult(Success("result")), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        var error = result.Error.ShouldBeOfType<UnauthorizedError>();
        error.PolicyName.ShouldBe("Admin");
    }

    private sealed record NonAuthorizedRequest;

    private sealed record AuthorizedResultRequest : IAuthorizedRequest
    {
        public string[] RequiredPolicies => ["Admin"];
    }

    private sealed record AuthorizedDirectRequest : IAuthorizedRequest
    {
        public string[] RequiredPolicies => ["Admin"];
    }

    private sealed record AuthorizedSinglePolicyRequest : IAuthorizedRequest
    {
        public string[] RequiredPolicies => ["Admin"];
    }

    private sealed record AuthorizedResultFailureRequest : IAuthorizedRequest
    {
        public string[] RequiredPolicies => ["Admin"];
    }

    private sealed record AuthorizedDirectFailureRequest : IAuthorizedRequest
    {
        public string[] RequiredPolicies => ["Admin"];
    }

    private sealed record AuthorizedMultiplePoliciesRequest : IAuthorizedRequest
    {
        public string[] RequiredPolicies => ["Admin", "Editor"];
    }

    private sealed record EmptyPoliciesRequest : IAuthorizedRequest
    {
        public string[] RequiredPolicies => [];
    }

    [Fact]
    public async Task Handle_WhenPolicyFails_ThrowsWithMessageContainingPolicyName()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user1")]));
        var authService = Substitute.For<IAuthorizationService>();
        authService.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(), "Manager")
            .Returns(Task.FromResult(AuthorizationResult.Failed()));
        var userAccessor = Substitute.For<ICurrentUserAccessor>();
        userAccessor.Current.Returns(principal);
        var behavior = new AuthorizationBehavior<AuthorizedDirectPolicyNameRequest, string>(authService, userAccessor);

        var ex = await Should.ThrowAsync<UnauthorizedAccessException>(
            () => behavior.Handle(new AuthorizedDirectPolicyNameRequest(), () => Task.FromResult("result"), CancellationToken.None));

        ex.Message.ShouldContain("Manager");
    }

    private sealed record AuthorizedResultPolicyNameRequest : IAuthorizedRequest
    {
        public string[] RequiredPolicies => ["Admin"];
    }

    private sealed record AuthorizedDirectPolicyNameRequest : IAuthorizedRequest
    {
        public string[] RequiredPolicies => ["Manager"];
    }
}
