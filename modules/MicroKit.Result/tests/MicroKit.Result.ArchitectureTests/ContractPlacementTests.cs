namespace MicroKit.Result.ArchitectureTests;

public sealed class ContractPlacementTests
{
    private static readonly Assembly CoreAssembly =
        typeof(MicroKit.Result.Result).Assembly;

    private static readonly Assembly AspNetCoreAssembly =
        typeof(MicroKit.Result.AspNetCore.ResultHttpExtensions).Assembly;

    // --- Core types live in the core assembly ---

    [Fact]
    public void Result_IsInCore()
    {
        typeof(Result).Assembly.ShouldBe(CoreAssembly);
    }

    [Fact]
    public void ResultOfT_IsInCore()
    {
        typeof(Result<>).Assembly.ShouldBe(CoreAssembly);
    }

    [Fact]
    public void Unit_IsInCore()
    {
        typeof(Unit).Assembly.ShouldBe(CoreAssembly);
    }

    [Fact]
    public void IError_IsInCore()
    {
        typeof(IError).Assembly.ShouldBe(CoreAssembly);
    }

    [Fact]
    public void Error_BaseRecord_IsInCore()
    {
        typeof(Error).Assembly.ShouldBe(CoreAssembly);
    }

    [Fact]
    public void ErrorCode_IsInCore()
    {
        typeof(ErrorCode).Assembly.ShouldBe(CoreAssembly);
    }

    [Fact]
    public void ErrorCollection_IsInCore()
    {
        typeof(ErrorCollection).Assembly.ShouldBe(CoreAssembly);
    }

    [Fact]
    public void ErrorMetadata_IsInCore()
    {
        typeof(ErrorMetadata).Assembly.ShouldBe(CoreAssembly);
    }

    [Fact]
    public void ErrorMetadataBuilder_IsInCore()
    {
        typeof(ErrorMetadataBuilder).Assembly.ShouldBe(CoreAssembly);
    }

    [Fact]
    public void ValidationError_IsInCore()
    {
        typeof(ValidationError).Assembly.ShouldBe(CoreAssembly);
    }

    [Fact]
    public void ValidationResult_IsInCore()
    {
        typeof(ValidationResult).Assembly.ShouldBe(CoreAssembly);
    }

    [Fact]
    public void ResultException_IsInCore()
    {
        typeof(ResultException).Assembly.ShouldBe(CoreAssembly);
    }

    [Fact]
    public void ResultFactory_IsInCore()
    {
        typeof(ResultFactory).Assembly.ShouldBe(CoreAssembly);
    }

    // --- Serialization types live in the core assembly, under MicroKit.Result.Serialization ---

    [Fact]
    public void ResultJsonConverterFactory_IsInCore_NotAspNetCore()
    {
        typeof(MicroKit.Result.Serialization.ResultJsonConverterFactory).Assembly.ShouldBe(CoreAssembly);
        typeof(MicroKit.Result.Serialization.ResultJsonConverterFactory).Assembly.ShouldNotBe(AspNetCoreAssembly);
    }

    [Fact]
    public void ResultJsonConverter_IsInCore_NotAspNetCore()
    {
        typeof(MicroKit.Result.Serialization.ResultJsonConverter<>).Assembly.ShouldBe(CoreAssembly);
        typeof(MicroKit.Result.Serialization.ResultJsonConverter<>).Assembly.ShouldNotBe(AspNetCoreAssembly);
    }

    // --- AspNetCore types live in the AspNetCore assembly, not in core ---

    [Fact]
    public void ResultHttpExtensions_IsInAspNetCore_NotCore()
    {
        typeof(MicroKit.Result.AspNetCore.ResultHttpExtensions).Assembly.ShouldBe(AspNetCoreAssembly);
        typeof(MicroKit.Result.AspNetCore.ResultHttpExtensions).Assembly.ShouldNotBe(CoreAssembly);
    }

    [Fact]
    public void ResultProblemDetailsFactory_IsInAspNetCore_NotCore()
    {
        typeof(MicroKit.Result.AspNetCore.ResultProblemDetailsFactory).Assembly.ShouldBe(AspNetCoreAssembly);
        typeof(MicroKit.Result.AspNetCore.ResultProblemDetailsFactory).Assembly.ShouldNotBe(CoreAssembly);
    }
}
