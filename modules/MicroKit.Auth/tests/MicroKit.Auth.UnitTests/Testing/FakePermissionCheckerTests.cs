namespace MicroKit.Auth.UnitTests.Testing;

public sealed class FakePermissionCheckerTests
{
    private static readonly Permission ReadPerm = Permission.Of("docs", "read");
    private static readonly Permission WritePerm = Permission.Of("docs", "write");

    [Fact]
    public async Task HasPermissionAsync_WhenNothingAllowed_ReturnsFalse()
    {
        var checker = new FakePermissionChecker();

        var result = await checker.HasPermissionAsync(ReadPerm);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeFalse();
    }

    [Fact]
    public async Task HasPermissionAsync_WhenPermissionAllowed_ReturnsTrue()
    {
        var checker = new FakePermissionChecker().Allow(ReadPerm);

        var result = await checker.HasPermissionAsync(ReadPerm);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_AfterDeny_ReturnsFalse()
    {
        var checker = new FakePermissionChecker()
            .Allow(ReadPerm)
            .Deny(ReadPerm);

        var result = await checker.HasPermissionAsync(ReadPerm);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeFalse();
    }

    [Fact]
    public async Task HasPermissionAsync_TenantOverload_WhenAllowed_ReturnsTrue()
    {
        var checker = new FakePermissionChecker().Allow(WritePerm);
        var tenantId = Guid.NewGuid();

        var result = await checker.HasPermissionAsync(tenantId, WritePerm);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_TenantOverload_WhenNotAllowed_ReturnsFalse()
    {
        var checker = new FakePermissionChecker();
        var tenantId = Guid.NewGuid();

        var result = await checker.HasPermissionAsync(tenantId, WritePerm);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeFalse();
    }
}
