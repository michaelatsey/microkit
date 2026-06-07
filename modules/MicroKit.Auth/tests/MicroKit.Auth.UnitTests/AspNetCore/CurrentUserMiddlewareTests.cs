namespace MicroKit.Auth.UnitTests;

public sealed class CurrentUserMiddlewareTests
{
    private static readonly Permission ReadPerm = Permission.Of("docs", "read");

    private readonly ICurrentUserAccessor _accessor = Substitute.For<ICurrentUserAccessor>();
    private readonly IClaimsMapper _mapper = Substitute.For<IClaimsMapper>();
    private readonly ILogger<CurrentUserMiddleware> _logger =
        Substitute.For<ILogger<CurrentUserMiddleware>>();
    private readonly RequestDelegate _next = Substitute.For<RequestDelegate>();

    private CurrentUserMiddleware CreateSut() => new(_next);

    private static DefaultHttpContext AuthenticatedContext()
    {
        var ctx = new DefaultHttpContext();
        var claims = new[] { new Claim("sub", Guid.NewGuid().ToString()), new Claim("email", "u@test.com") };
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        return ctx;
    }

    private static DefaultHttpContext AnonymousContext() => new();

    [Fact]
    public async Task InvokeAsync_WhenUserIsAuthenticated_SetsCurrentUserOnAccessor()
    {
        var httpContext = AuthenticatedContext();
        var user = FakeCurrentUserBuilder.Create().Build();
        _mapper.MapFromClaims(Arg.Any<ClaimsPrincipal>())
            .Returns(Success<ICurrentUser>(user));

        await CreateSut().InvokeAsync(httpContext, _accessor, _mapper, _logger);

        _accessor.Received(1).Set(user);
        await _next.Received(1).Invoke(httpContext);
    }

    [Fact]
    public async Task InvokeAsync_WhenUserIsAnonymous_DoesNotCallMapper()
    {
        var httpContext = AnonymousContext();

        await CreateSut().InvokeAsync(httpContext, _accessor, _mapper, _logger);

        _mapper.DidNotReceiveWithAnyArgs().MapFromClaims(default!);
        _accessor.DidNotReceiveWithAnyArgs().Set(default!);
        await _next.Received(1).Invoke(httpContext);
    }

    [Fact]
    public async Task InvokeAsync_WhenMappingFails_DoesNotSetAccessorAndCallsNext()
    {
        var httpContext = AuthenticatedContext();
        _mapper.MapFromClaims(Arg.Any<ClaimsPrincipal>())
            .Returns(Failure<ICurrentUser>(new ClaimsMappingError("sub")));

        await CreateSut().InvokeAsync(httpContext, _accessor, _mapper, _logger);

        _accessor.DidNotReceiveWithAnyArgs().Set(default!);
        await _next.Received(1).Invoke(httpContext);
    }
}
