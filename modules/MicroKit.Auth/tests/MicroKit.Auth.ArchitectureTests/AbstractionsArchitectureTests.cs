namespace MicroKit.Auth.ArchitectureTests;

public sealed class AbstractionsArchitectureTests
{
    private static readonly System.Reflection.Assembly Assembly = typeof(ICurrentUser).Assembly;

    [Fact]
    public void Abstractions_HasNoAspNetCoreDependency()
    {
        Types.InAssembly(Assembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.AspNetCore")
            .GetResult()
            .IsSuccessful
            .ShouldBeTrue();
    }

    [Fact]
    public void Abstractions_HasNoEntityFrameworkCoreDependency()
    {
        Types.InAssembly(Assembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult()
            .IsSuccessful
            .ShouldBeTrue();
    }

    [Fact]
    public void Abstractions_HasNoMicrosoftIdentityModelDependency()
    {
        Types.InAssembly(Assembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.IdentityModel")
            .GetResult()
            .IsSuccessful
            .ShouldBeTrue();
    }

    [Fact]
    public void Abstractions_HasNoSystemIdentityModelDependency()
    {
        Types.InAssembly(Assembly)
            .ShouldNot()
            .HaveDependencyOn("System.IdentityModel.Tokens.Jwt")
            .GetResult()
            .IsSuccessful
            .ShouldBeTrue();
    }
}
