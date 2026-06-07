namespace MicroKit.Auth.ArchitectureTests;

public sealed class CoreArchitectureTests
{
    private static readonly System.Reflection.Assembly Assembly = typeof(CurrentUser).Assembly;

    [Fact]
    public void Core_HasNoAspNetCoreDependency()
    {
        Types.InAssembly(Assembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.AspNetCore")
            .GetResult()
            .IsSuccessful
            .ShouldBeTrue();
    }

    [Fact]
    public void Core_HasNoEntityFrameworkCoreDependency()
    {
        Types.InAssembly(Assembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult()
            .IsSuccessful
            .ShouldBeTrue();
    }

    [Fact]
    public void Core_HasNoMicrosoftIdentityModelDependency()
    {
        Types.InAssembly(Assembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.IdentityModel")
            .GetResult()
            .IsSuccessful
            .ShouldBeTrue();
    }
}
