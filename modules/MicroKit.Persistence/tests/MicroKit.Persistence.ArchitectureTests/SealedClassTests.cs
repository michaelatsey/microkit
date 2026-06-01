namespace MicroKit.Persistence.ArchitectureTests;

public sealed class SealedClassTests
{
    // IsSealed on open generic type definitions always returns false in the CLR via Type.IsSealed.
    // TypeAttributes.Sealed is the correct way to check sealed for both open generic and non-generic types.
    private static bool IsSealed(Type type) =>
        (type.Attributes & TypeAttributes.Sealed) != 0;

    [Fact]
    public void EfUnitOfWork_IsSealed()
    {
        IsSealed(typeof(MicroKit.Persistence.EntityFrameworkCore.EfUnitOfWork<>)).ShouldBeTrue();
    }

    [Fact]
    public void EfSpecificationEvaluator_IsSealed()
    {
        IsSealed(typeof(MicroKit.Persistence.EntityFrameworkCore.EfSpecificationEvaluator)).ShouldBeTrue();
    }

    [Fact]
    public void InMemoryRepository_IsSealed()
    {
        IsSealed(typeof(MicroKit.Persistence.Testing.InMemoryRepository<>)).ShouldBeTrue();
    }

    [Fact]
    public void InMemoryReadRepository_IsSealed()
    {
        IsSealed(typeof(MicroKit.Persistence.Testing.InMemoryReadRepository<>)).ShouldBeTrue();
    }

    [Fact]
    public void InMemoryUnitOfWork_IsSealed()
    {
        IsSealed(typeof(MicroKit.Persistence.Testing.InMemoryUnitOfWork)).ShouldBeTrue();
    }

    [Fact]
    public void QueryOptions_IsSealed()
    {
        IsSealed(typeof(MicroKit.Persistence.QueryOptions<>)).ShouldBeTrue();
    }

    [Fact]
    public void PagedResult_IsSealed()
    {
        IsSealed(typeof(MicroKit.Persistence.PagedResult<>)).ShouldBeTrue();
    }

    [Fact]
    public void PaginationOptions_IsSealed()
    {
        IsSealed(typeof(MicroKit.Persistence.PaginationOptions)).ShouldBeTrue();
    }

    // EfRepository<,> is intentionally not sealed — consumers subclass it for typed repositories.
    [Fact]
    public void EfRepository_BaseClass_IsNotSealed()
    {
        IsSealed(typeof(MicroKit.Persistence.EntityFrameworkCore.EfRepository<,>)).ShouldBeFalse();
    }

    [Fact]
    public void EfReadRepository_BaseClass_IsNotSealed()
    {
        // Open generic form typeof(EfReadRepository<,>) used — two type params.
        IsSealed(typeof(MicroKit.Persistence.EntityFrameworkCore.EfReadRepository<,>)).ShouldBeFalse();
    }
}
