namespace MicroKit.Messaging.ArchitectureTests;

// Full architecture tests added after all packages implemented

public sealed class MessagingAbstractionsArchitectureTests
{
    private static readonly Assembly AbstractionsAssembly = typeof(IIntegrationEvent).Assembly;

    [Fact]
    public void Abstractions_HasNoEfCoreDependency()
    {
        Types.InAssembly(AbstractionsAssembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult()
            .IsSuccessful
            .ShouldBeTrue();
    }

    [Fact]
    public void Abstractions_HasNoAspNetCoreDependency()
    {
        Types.InAssembly(AbstractionsAssembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.AspNetCore")
            .GetResult()
            .IsSuccessful
            .ShouldBeTrue();
    }

    [Fact]
    public void Abstractions_HasNoMediatRContractsDependency()
    {
        Types.InAssembly(AbstractionsAssembly)
            .ShouldNot()
            .HaveDependencyOn("MediatR.Contracts")
            .GetResult()
            .IsSuccessful
            .ShouldBeTrue();
    }

    [Fact]
    public void Abstractions_HasNoMediatRDependency()
    {
        Types.InAssembly(AbstractionsAssembly)
            .ShouldNot()
            .HaveDependencyOn("MediatR")
            .GetResult()
            .IsSuccessful
            .ShouldBeTrue();
    }

    [Fact]
    public void Placeholder_AlwaysPasses()
    {
        // Placeholder — full cross-package architecture tests will be added
        // once all MicroKit.Messaging packages (Core, EFCore, Testing) are implemented.
        true.ShouldBeTrue();
    }
}
