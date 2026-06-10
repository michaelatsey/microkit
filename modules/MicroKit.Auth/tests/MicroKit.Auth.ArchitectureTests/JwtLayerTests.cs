using System.Reflection;

namespace MicroKit.Auth.ArchitectureTests;

public sealed class JwtLayerTests
{
    private static readonly Assembly JwtAssembly = typeof(JwtValidator).Assembly;

    [Fact]
    public void Jwt_HasNoAspNetCoreDependency()
    {
        Types.InAssembly(JwtAssembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.AspNetCore")
            .GetResult()
            .IsSuccessful
            .ShouldBeTrue();
    }

    [Fact]
    public void Jwt_HasNoEntityFrameworkCoreDependency()
    {
        Types.InAssembly(JwtAssembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult()
            .IsSuccessful
            .ShouldBeTrue();
    }

    [Fact]
    public void Jwt_HasNoDependencyOnAuthCore()
    {
        // Jwt may reference MicroKit.Auth.Abstractions (same namespace "MicroKit.Auth")
        // but must never reference MicroKit.Auth (Core). NetArchTest namespace-prefix matching
        // cannot distinguish the two, so we use direct assembly reference inspection instead.
        var coreAssemblyName = typeof(CurrentUser).Assembly.GetName().Name;
        var referencedNames = JwtAssembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();

        referencedNames.ShouldNotContain(coreAssemblyName);
    }

    [Fact]
    public void Jwt_HasMicrosoftIdentityModelDependency()
    {
        // Positive check: Jwt must depend on Microsoft.IdentityModel packages (HS256 impl)
        var referencedNames = JwtAssembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToList();

        referencedNames.ShouldContain(name => name.StartsWith("Microsoft.IdentityModel", StringComparison.Ordinal));
    }

    [Fact]
    public void Jwt_PublicClasses_AreSealed()
    {
        var result = Types.InAssembly(JwtAssembly)
            .That()
            .ArePublic()
            .And()
            .AreClasses()
            .Should()
            .BeSealed()
            .GetResult();

        result.IsSuccessful.ShouldBeTrue();
    }
}
