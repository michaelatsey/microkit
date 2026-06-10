namespace MicroKit.Auth.UnitTests.Permissions;

public sealed class PermissionRegistryTests
{
    private static readonly Permission ReadPerm = Permission.Of("docs", "read");
    private static readonly Permission CreatePerm = Permission.Of("docs", "create");
    private static readonly Permission UpdatePerm = Permission.Of("docs", "update");

    [Fact]
    public void All_BeforeAnyRegistration_ReturnsEmpty()
    {
        var registry = new PermissionRegistry();

        registry.All.ShouldBeEmpty();
    }

    [Fact]
    public void Register_WhenCalledWithSinglePermission_PopulatesAll()
    {
        var registry = new PermissionRegistry();

        registry.Register(ReadPerm);

        registry.All.Count.ShouldBe(1);
        registry.All.ShouldContain(ReadPerm);
    }

    [Fact]
    public void Register_WhenCalledWithMultiplePermissions_PopulatesAll()
    {
        var registry = new PermissionRegistry();

        registry.Register(ReadPerm, CreatePerm);

        registry.All.Count.ShouldBe(2);
        registry.All.ShouldContain(ReadPerm);
        registry.All.ShouldContain(CreatePerm);
    }

    [Fact]
    public void Register_WhenCalledWithDuplicate_DoesNotDuplicateEntry()
    {
        var registry = new PermissionRegistry();

        registry.Register(ReadPerm, ReadPerm);

        registry.All.Count.ShouldBe(1);
    }

    [Fact]
    public void Register_WhenCalledMultipleTimes_AccumulatesPermissions()
    {
        var registry = new PermissionRegistry();

        registry.Register(ReadPerm);
        registry.Register(CreatePerm);
        registry.Register(UpdatePerm);

        registry.All.Count.ShouldBe(3);
    }

    [Fact]
    public void Register_WhenCalledMultipleTimesWithOverlappingPermissions_DoesNotDuplicate()
    {
        var registry = new PermissionRegistry();

        registry.Register(ReadPerm);
        registry.Register(ReadPerm, CreatePerm);

        registry.All.Count.ShouldBe(2);
    }

    [Fact]
    public void Register_ReturnsRegistryInstanceForFluentChaining()
    {
        var registry = new PermissionRegistry();

        var returned = registry.Register(ReadPerm);

        returned.ShouldBeSameAs(registry);
    }

    [Fact]
    public void Register_WhenCalledWithNullArray_ThrowsArgumentNullException()
    {
        var registry = new PermissionRegistry();

        Should.Throw<ArgumentNullException>(() => registry.Register(null!));
    }

    [Fact]
    public void All_AfterRegister_ReflectsCurrentState()
    {
        var registry = new PermissionRegistry();

        registry.All.ShouldBeEmpty();
        registry.Register(ReadPerm);
        registry.All.Count.ShouldBe(1);
        registry.Register(CreatePerm);
        registry.All.Count.ShouldBe(2);
    }

    [Fact]
    public void Contains_WhenPermissionIsRegistered_ReturnsTrue()
    {
        var registry = new PermissionRegistry();
        registry.Register(ReadPerm);

        registry.Contains(ReadPerm).ShouldBeTrue();
    }

    [Fact]
    public void Contains_WhenPermissionIsNotRegistered_ReturnsFalse()
    {
        var registry = new PermissionRegistry();

        registry.Contains(ReadPerm).ShouldBeFalse();
    }

    [Fact]
    public void Contains_WhenPermissionRegisteredThenCheckedWithEqualValue_ReturnsTrue()
    {
        var registry = new PermissionRegistry();
        registry.Register(Permission.Of("docs", "read"));

        // Different instance, same value — record structural equality
        registry.Contains(Permission.Of("docs", "read")).ShouldBeTrue();
    }
}
