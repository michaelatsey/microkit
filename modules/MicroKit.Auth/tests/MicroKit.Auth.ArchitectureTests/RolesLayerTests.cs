namespace MicroKit.Auth.ArchitectureTests;

public sealed class RolesLayerTests
{
    private static readonly System.Reflection.Assembly Assembly = typeof(RoleRegistry).Assembly;

    [Fact]
    public void Roles_ShouldHave_ZeroAspNetCoreDependency()
    {
        Types.InAssembly(Assembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.AspNetCore")
            .GetResult()
            .IsSuccessful
            .ShouldBeTrue();
    }

    [Fact]
    public void Roles_ShouldHave_ZeroEntityFrameworkCoreDependency()
    {
        Types.InAssembly(Assembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult()
            .IsSuccessful
            .ShouldBeTrue();
    }

    [Fact]
    public void Roles_ShouldNotDependOn_AuthCore()
    {
        // Roles may reference MicroKit.Auth.Abstractions (same namespace "MicroKit.Auth")
        // but must never reference MicroKit.Auth (Core). NetArchTest namespace-prefix matching
        // cannot distinguish the two, so we use direct assembly reference inspection instead.
        var coreAssemblyName = typeof(CurrentUser).Assembly.GetName().Name;
        var referencedNames = Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();

        referencedNames.ShouldNotContain(coreAssemblyName);
    }

    [Fact]
    public void InMemoryRoleStore_ShouldBeInternal()
    {
        var type = Assembly.GetType("MicroKit.Auth.Roles.InMemoryRoleStore");

        type.ShouldNotBeNull();
        type!.IsPublic.ShouldBeFalse();
    }

    [Fact]
    public void InMemoryRolePermissionMap_ShouldBeInternal()
    {
        var type = Assembly.GetType("MicroKit.Auth.Roles.InMemoryRolePermissionMap");

        type.ShouldNotBeNull();
        type!.IsPublic.ShouldBeFalse();
    }
}
