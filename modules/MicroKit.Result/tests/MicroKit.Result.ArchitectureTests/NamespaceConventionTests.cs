// ResideInNamespace() performs a prefix match: "MicroKit.Result" also covers
// sub-namespaces like MicroKit.Result.Serialization.

namespace MicroKit.Result.ArchitectureTests;

public sealed class NamespaceConventionTests
{
    private static readonly Assembly CoreAssembly =
        typeof(MicroKit.Result.Result).Assembly;

    private static readonly Assembly AspNetCoreAssembly =
        typeof(MicroKit.Result.AspNetCore.ResultHttpExtensions).Assembly;

    [Fact]
    public void CorePublicTypes_ResideInCorrectNamespace()
    {
        var result = Types.InAssembly(CoreAssembly)
            .That().ArePublic()
            .Should().ResideInNamespace("MicroKit.Result")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(result.FailingTypeNames?.ToString() ?? string.Empty);
    }

    [Fact]
    public void AspNetCorePublicTypes_ResideInCorrectNamespace()
    {
        var result = Types.InAssembly(AspNetCoreAssembly)
            .That().ArePublic()
            .Should().ResideInNamespace("MicroKit.Result.AspNetCore")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(result.FailingTypeNames?.ToString() ?? string.Empty);
    }

    [Fact]
    public void ExtensionClasses_InCoreAssembly_AreStatic()
    {
        var result = Types.InAssembly(CoreAssembly)
            .That().HaveNameEndingWith("Extensions")
            .Should().BeStatic()
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(result.FailingTypeNames?.ToString() ?? string.Empty);
    }

    [Fact]
    public void ExtensionClasses_InAspNetCoreAssembly_AreStatic()
    {
        var result = Types.InAssembly(AspNetCoreAssembly)
            .That().HaveNameEndingWith("Extensions")
            .Should().BeStatic()
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(result.FailingTypeNames?.ToString() ?? string.Empty);
    }

    [Fact]
    public void ResultFactory_IsStatic()
    {
        // ResultFactory is a pure static helper — never instantiated.
        // Note: ResultJsonConverterFactory is NOT static; it extends JsonConverterFactory and must be
        // instantiated by the JSON serializer infrastructure. No blanket "Factory == static" rule applies here.
        var t = typeof(ResultFactory);
        (t.Attributes & TypeAttributes.Abstract).ShouldNotBe((TypeAttributes)0,
            "ResultFactory must be abstract (static class)");
        (t.Attributes & TypeAttributes.Sealed).ShouldNotBe((TypeAttributes)0,
            "ResultFactory must be sealed (static class)");
    }
}
