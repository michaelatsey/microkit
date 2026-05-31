using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MicroKit.MediatR;
using MicroKit.MediatR.Behaviors;
using MicroKit.MediatR.Behaviors.Idempotency;
using MicroKit.MediatR.Behaviors.Pipeline;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MicroKit.MediatR.UnitTests.Pipeline;

/// <summary>
/// Verifies that <see cref="PipelineOrder"/> constants are correct (contract test —
/// changing these values is a breaking change) and that each behavior's Order property
/// matches its canonical constant, ensuring registration order == execution order.
/// </summary>
public sealed class PipelineOrderTests
{
    // ── Constant value assertions ─────────────────────────────────────────

    [Fact] public void Logging_Constant_Is_100() => PipelineOrder.Logging.ShouldBe(100);
    [Fact] public void Authorization_Constant_Is_200() => PipelineOrder.Authorization.ShouldBe(200);
    [Fact] public void Validation_Constant_Is_300() => PipelineOrder.Validation.ShouldBe(300);
    [Fact] public void Idempotency_Constant_Is_400() => PipelineOrder.Idempotency.ShouldBe(400);
    [Fact] public void Caching_Constant_Is_500() => PipelineOrder.Caching.ShouldBe(500);
    [Fact] public void Retry_Constant_Is_600() => PipelineOrder.Retry.ShouldBe(600);

    // ── Behavior.Order == PipelineOrder constant ──────────────────────────

    [Fact]
    public void LoggingBehavior_Order_MatchesLoggingConstant()
    {
        var behavior = new LoggingBehavior<OrderRequest, string>(
            NullLogger<LoggingBehavior<OrderRequest, string>>.Instance);
        behavior.Order.ShouldBe(PipelineOrder.Logging);
    }

    [Fact]
    public void AuthorizationBehavior_Order_MatchesAuthorizationConstant()
    {
        var behavior = new AuthorizationBehavior<OrderRequest, string>(
            Substitute.For<IAuthorizationService>(),
            Substitute.For<ICurrentUserAccessor>());
        behavior.Order.ShouldBe(PipelineOrder.Authorization);
    }

    [Fact]
    public void ValidationBehavior_Order_MatchesValidationConstant()
    {
        var behavior = new ValidationBehavior<OrderRequest, string>([]);
        behavior.Order.ShouldBe(PipelineOrder.Validation);
    }

    [Fact]
    public void IdempotencyBehavior_Order_MatchesIdempotencyConstant()
    {
        var behavior = new IdempotencyBehavior<OrderRequest, string>(
            Substitute.For<IIdempotencyStore>());
        behavior.Order.ShouldBe(PipelineOrder.Idempotency);
    }

    [Fact]
    public void CachingBehavior_Order_MatchesCachingConstant()
    {
        var behavior = new CachingBehavior<OrderRequest, string>(
            Substitute.For<IDistributedCache>(),
            Options.Create(new System.Text.Json.JsonSerializerOptions()),
            NullLogger<CachingBehavior<OrderRequest, string>>.Instance);
        behavior.Order.ShouldBe(PipelineOrder.Caching);
    }

    [Fact]
    public void RetryBehavior_Order_MatchesRetryConstant()
    {
        var behavior = new RetryBehavior<OrderRequest, string>();
        behavior.Order.ShouldBe(PipelineOrder.Retry);
    }

    [Fact]
    public void PipelineOrder_Constants_AreStrictlyAscending()
    {
        PipelineOrder.Logging.ShouldBeLessThan(PipelineOrder.Authorization);
        PipelineOrder.Authorization.ShouldBeLessThan(PipelineOrder.Validation);
        PipelineOrder.Validation.ShouldBeLessThan(PipelineOrder.Idempotency);
        PipelineOrder.Idempotency.ShouldBeLessThan(PipelineOrder.Caching);
        PipelineOrder.Caching.ShouldBeLessThan(PipelineOrder.Retry);
    }

    private sealed record OrderRequest;
}
