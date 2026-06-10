namespace MicroKit.Auth.UnitTests.Roles;

public sealed class RoleRegistryTests
{
    private readonly RoleRegistry _sut = new();

    [Fact]
    public void All_WhenEmpty_ReturnsEmptyList()
    {
        _sut.All.ShouldBeEmpty();
    }

    [Fact]
    public void Register_WhenCalledWithOneRole_AddsToAll()
    {
        _sut.Register(SystemRoles.Admin);

        _sut.All.Count.ShouldBe(1);
        _sut.All.ShouldContain(SystemRoles.Admin);
    }

    [Fact]
    public void Register_WhenCalledWithDuplicate_DoesNotDuplicateEntry()
    {
        _sut.Register(SystemRoles.Admin);
        _sut.Register(SystemRoles.Admin);

        _sut.All.Count.ShouldBe(1);
    }

    [Fact]
    public void Register_WhenCalledWithNull_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => _sut.Register(null!));
    }

    [Fact]
    public void Register_ReturnsThisForFluentChaining()
    {
        var returned = _sut.Register(SystemRoles.Viewer);

        returned.ShouldBeSameAs(_sut);
    }

    [Fact]
    public void Contains_WhenRoleRegistered_ReturnsTrue()
    {
        _sut.Register(SystemRoles.Auditor);

        _sut.Contains(SystemRoles.Auditor).ShouldBeTrue();
    }

    [Fact]
    public void Contains_WhenRoleNotRegistered_ReturnsFalse()
    {
        _sut.Contains(SystemRoles.Admin).ShouldBeFalse();
    }

    [Fact]
    public void All_ReflectsSubsequentRegisterCalls()
    {
        var snapshot = _sut.All;
        _sut.Register(SystemRoles.Manager);

        snapshot.Count.ShouldBe(1);
        snapshot.ShouldContain(SystemRoles.Manager);
    }
}
