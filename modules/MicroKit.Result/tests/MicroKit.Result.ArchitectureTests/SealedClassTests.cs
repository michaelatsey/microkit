namespace MicroKit.Result.ArchitectureTests;

public sealed class SealedClassTests
{
    // IsSealed on open generic type definitions always returns false in the CLR via Type.IsSealed.
    // TypeAttributes.Sealed is the correct way to check sealed for both open generic and non-generic types.
    private static bool IsSealed(Type type) =>
        (type.Attributes & TypeAttributes.Sealed) != 0;

    // --- Concrete error types must be sealed ---

    [Fact]
    public void ErrorCollection_IsSealed()
    {
        IsSealed(typeof(ErrorCollection)).ShouldBeTrue();
    }

    [Fact]
    public void ErrorMetadataBuilder_IsSealed()
    {
        IsSealed(typeof(ErrorMetadataBuilder)).ShouldBeTrue();
    }

    [Fact]
    public void ValidationError_IsSealed()
    {
        IsSealed(typeof(ValidationError)).ShouldBeTrue();
    }

    [Fact]
    public void ValidationResult_IsSealed()
    {
        IsSealed(typeof(ValidationResult)).ShouldBeTrue();
    }

    [Fact]
    public void ResultException_IsSealed()
    {
        IsSealed(typeof(ResultException)).ShouldBeTrue();
    }

    // --- Serialization types must be sealed ---

    [Fact]
    public void ResultJsonConverterFactory_IsSealed()
    {
        IsSealed(typeof(MicroKit.Result.Serialization.ResultJsonConverterFactory)).ShouldBeTrue();
    }

    [Fact]
    public void ResultJsonConverter_IsSealed()
    {
        // Open generic — TypeAttributes.Sealed is required; Type.IsSealed returns false for open generics.
        IsSealed(typeof(MicroKit.Result.Serialization.ResultJsonConverter<>)).ShouldBeTrue();
    }

    // --- AspNetCore extension types ---

    [Fact]
    public void ResultProblemDetailsFactory_IsStatic()
    {
        // Static classes are both abstract and sealed at the IL level.
        var t = typeof(MicroKit.Result.AspNetCore.ResultProblemDetailsFactory);
        IsSealed(t).ShouldBeTrue();
        (t.Attributes & TypeAttributes.Abstract).ShouldNotBe((TypeAttributes)0);
    }

    // --- Error base record is intentionally NOT sealed: consumers must subclass it ---

    [Fact]
    public void Error_BaseRecord_IsNotSealed()
    {
        // abstract record Error — designed as an open base for domain error hierarchies.
        IsSealed(typeof(Error)).ShouldBeFalse();
    }
}
