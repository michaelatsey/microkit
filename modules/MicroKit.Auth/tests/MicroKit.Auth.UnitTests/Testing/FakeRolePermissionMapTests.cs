namespace MicroKit.Auth.UnitTests.Testing;

public sealed class FakeRolePermissionMapTests
{
    private static readonly Role AdminRole = Role.Of("admin");
    private static readonly Role AuditorRole = Role.Of("auditor");
    private static readonly Permission ReadPerm = Permission.Of("docs", "read");
    private static readonly Permission WritePerm = Permission.Of("docs", "write");
    private static readonly Permission DeletePerm = Permission.Of("docs", "delete");

    [Fact]
    public void GetPermissionsForRole_WhenUnknownRole_ReturnsEmptyList()
    {
        var map = new FakeRolePermissionMap();

        var result = map.GetPermissionsForRole(Role.Of("unknown"));

        result.ShouldBeEmpty();
    }

    [Fact]
    public void GetPermissionsForRole_WhenSingleMapped_ReturnsPermission()
    {
        var map = new FakeRolePermissionMap().Map(AdminRole, ReadPerm);

        var result = map.GetPermissionsForRole(AdminRole);

        result.Count.ShouldBe(1);
        result.ShouldContain(ReadPerm);
    }

    [Fact]
    public void Map_WithMultiplePermissions_AddsAll()
    {
        var map = new FakeRolePermissionMap()
            .Map(AdminRole, ReadPerm, WritePerm, DeletePerm);

        var result = map.GetPermissionsForRole(AdminRole);

        result.Count.ShouldBe(3);
        result.ShouldContain(ReadPerm);
        result.ShouldContain(WritePerm);
        result.ShouldContain(DeletePerm);
    }

    [Fact]
    public void Clear_ResetsAllMappings()
    {
        var map = new FakeRolePermissionMap()
            .Map(AdminRole, ReadPerm, WritePerm)
            .Map(AuditorRole, ReadPerm)
            .Clear();

        map.GetPermissionsForRole(AdminRole).ShouldBeEmpty();
        map.GetPermissionsForRole(AuditorRole).ShouldBeEmpty();
    }

    [Fact]
    public void GetPermissionsForRole_DifferentRoles_AreIsolated()
    {
        var map = new FakeRolePermissionMap()
            .Map(AdminRole, ReadPerm, WritePerm)
            .Map(AuditorRole, ReadPerm);

        var adminPerms = map.GetPermissionsForRole(AdminRole);
        var auditorPerms = map.GetPermissionsForRole(AuditorRole);

        adminPerms.Count.ShouldBe(2);
        adminPerms.ShouldContain(WritePerm);

        auditorPerms.Count.ShouldBe(1);
        auditorPerms.ShouldNotContain(WritePerm);
    }
}
