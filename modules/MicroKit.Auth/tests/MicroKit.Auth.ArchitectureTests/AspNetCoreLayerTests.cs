namespace MicroKit.Auth.ArchitectureTests;

public sealed class AspNetCoreLayerTests
{
    private static readonly System.Reflection.Assembly Assembly =
        typeof(CurrentUserMiddleware).Assembly;

    [Fact]
    public void AspNetCore_HasNoEntityFrameworkCoreDependency()
    {
        Types.InAssembly(Assembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult()
            .IsSuccessful
            .ShouldBeTrue();
    }

    [Fact]
    public void AspNetCore_HasNoMicrosoftIdentityModelDependency()
    {
        Types.InAssembly(Assembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.IdentityModel")
            .GetResult()
            .IsSuccessful
            .ShouldBeTrue();
    }

    [Fact]
    public void AspNetCore_HasNoSystemIdentityModelJwtDependency()
    {
        Types.InAssembly(Assembly)
            .ShouldNot()
            .HaveDependencyOn("System.IdentityModel.Tokens.Jwt")
            .GetResult()
            .IsSuccessful
            .ShouldBeTrue();
    }
}
