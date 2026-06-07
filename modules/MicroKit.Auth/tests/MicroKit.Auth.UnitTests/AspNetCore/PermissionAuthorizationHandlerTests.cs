namespace MicroKit.Auth.UnitTests;

public sealed class PermissionAuthorizationHandlerTests
{
    private static readonly Permission ReadPerm = Permission.Of("docs", "read");

    private static (PermissionAuthorizationHandler handler, IPermissionChecker checker, DefaultHttpContext httpContext)
        CreateSut()
    {
        var checker = Substitute.For<IPermissionChecker>();
        var httpContext = new DefaultHttpContext();

        // Wire a minimal service provider so RequestServices resolves IPermissionChecker
        var services = new ServiceCollection();
        services.AddSingleton<IPermissionChecker>(checker);
        httpContext.RequestServices = services.BuildServiceProvider();

        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns(httpContext);

        return (new PermissionAuthorizationHandler(httpContextAccessor), checker, httpContext);
    }

    private static AuthorizationHandlerContext BuildContext(Permission permission)
    {
        var requirement = new PermissionAuthorizationRequirement(permission);
        var user = new ClaimsPrincipal(new ClaimsIdentity([], "test"));
        return new AuthorizationHandlerContext([requirement], user, null);
    }

    [Fact]
    public async Task HandleAsync_WhenUserHasPermission_SucceedsRequirement()
    {
        var (handler, checker, _) = CreateSut();
        checker.HasPermissionAsync(ReadPerm, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Success(true)));
        var context = BuildContext(ReadPerm);

        await handler.HandleAsync(context);

        context.HasSucceeded.ShouldBeTrue();
    }

    [Fact]
    public async Task HandleAsync_WhenUserLacksPermission_DoesNotSucceedRequirement()
    {
        var (handler, checker, _) = CreateSut();
        checker.HasPermissionAsync(ReadPerm, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Success(false)));
        var context = BuildContext(ReadPerm);

        await handler.HandleAsync(context);

        context.HasSucceeded.ShouldBeFalse();
    }

    [Fact]
    public async Task HandleAsync_WhenCheckerReturnsFailure_DoesNotSucceedRequirement()
    {
        var (handler, checker, _) = CreateSut();
        checker.HasPermissionAsync(ReadPerm, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Failure<bool>(new UnauthenticatedError())));
        var context = BuildContext(ReadPerm);

        await handler.HandleAsync(context);

        context.HasSucceeded.ShouldBeFalse();
    }

    [Fact]
    public async Task HandleAsync_WhenHttpContextIsNull_DoesNotSucceedRequirement()
    {
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns((HttpContext?)null);
        var handler = new PermissionAuthorizationHandler(httpContextAccessor);
        var context = BuildContext(ReadPerm);

        await handler.HandleAsync(context);

        context.HasSucceeded.ShouldBeFalse();
    }
}
