namespace MicroKit.Auth.ArchitectureTests;

public sealed class PermissionsLayerTests
{
    private static readonly System.Reflection.Assembly Assembly = typeof(PermissionRegistry).Assembly;

    [Fact]
    public void Permissions_HasNoAspNetCoreDependency()
    {
        Types.InAssembly(Assembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.AspNetCore")
            .GetResult()
            .IsSuccessful
            .ShouldBeTrue();
    }

    [Fact]
    public void Permissions_HasNoEntityFrameworkCoreDependency()
    {
        Types.InAssembly(Assembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult()
            .IsSuccessful
            .ShouldBeTrue();
    }

    [Fact]
    public void Permissions_HasNoMicrosoftIdentityModelDependency()
    {
        Types.InAssembly(Assembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.IdentityModel")
            .GetResult()
            .IsSuccessful
            .ShouldBeTrue();
    }

    [Fact]
    public void Permissions_HasNoDependencyOnAuthCore()
    {
        // Permissions may reference MicroKit.Auth.Abstractions (same namespace "MicroKit.Auth")
        // but must never reference MicroKit.Auth (Core). NetArchTest namespace-prefix matching
        // cannot distinguish the two, so we use direct assembly reference inspection instead.
        var coreAssemblyName = typeof(CurrentUser).Assembly.GetName().Name;
        var referencedNames = Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();

        referencedNames.ShouldNotContain(coreAssemblyName);
    }
}
