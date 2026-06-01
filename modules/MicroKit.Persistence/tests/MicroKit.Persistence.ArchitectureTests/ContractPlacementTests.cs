namespace MicroKit.Persistence.ArchitectureTests;

public sealed class ContractPlacementTests
{
    private static readonly Assembly AbstractionsAssembly =
        typeof(MicroKit.Persistence.Abstractions.IUnitOfWork).Assembly;

    private static readonly Assembly CoreAssembly =
        typeof(MicroKit.Persistence.ISpecificationEvaluator).Assembly;

    private static readonly Assembly EfCoreAssembly =
        typeof(MicroKit.Persistence.EntityFrameworkCore.ITransactionalUnitOfWork).Assembly;

    private static readonly Assembly TestingAssembly =
        typeof(MicroKit.Persistence.Testing.InMemoryRepository<>).Assembly;

    // --- In Abstractions ---

    [Fact]
    public void IUnitOfWork_IsInAbstractions()
    {
        typeof(MicroKit.Persistence.Abstractions.IUnitOfWork).Assembly.ShouldBe(AbstractionsAssembly);
    }

    [Fact]
    public void ITransactionalContext_IsInAbstractions()
    {
        typeof(MicroKit.Persistence.Abstractions.ITransactionalContext).Assembly.ShouldBe(AbstractionsAssembly);
    }

    [Fact]
    public void IPagedResult_IsInAbstractions()
    {
        typeof(MicroKit.Persistence.Abstractions.IPagedResult<>).Assembly.ShouldBe(AbstractionsAssembly);
    }

    [Fact]
    public void IRepository_IsInAbstractions()
    {
        typeof(MicroKit.Persistence.Abstractions.IRepository<>).Assembly.ShouldBe(AbstractionsAssembly);
    }

    [Fact]
    public void IReadRepository_Marker_IsInAbstractions()
    {
        // NOTE 1: The Abstractions marker IReadRepository<T> is intentionally empty.
        typeof(MicroKit.Persistence.Abstractions.IReadRepository<>).Assembly.ShouldBe(AbstractionsAssembly);
    }

    [Fact]
    public void PersistenceException_IsInAbstractions()
    {
        typeof(MicroKit.Persistence.Abstractions.PersistenceException).Assembly.ShouldBe(AbstractionsAssembly);
    }

    [Fact]
    public void IOutboxStore_IsInAbstractions()
    {
        typeof(MicroKit.Persistence.Abstractions.IOutboxStore).Assembly.ShouldBe(AbstractionsAssembly);
    }

    // --- In Core (not Abstractions) ---

    [Fact]
    public void IReadRepository_Full_IsInCore()
    {
        // NOTE 1: Core's IReadRepository<T> extends the Abstractions marker and adds query methods.
        typeof(MicroKit.Persistence.IReadRepository<>).Assembly.ShouldBe(CoreAssembly);
        typeof(MicroKit.Persistence.IReadRepository<>).Assembly.ShouldNotBe(AbstractionsAssembly);
    }

    [Fact]
    public void ISpecificationEvaluator_IsInCore_NotAbstractions()
    {
        typeof(MicroKit.Persistence.ISpecificationEvaluator).Assembly.ShouldBe(CoreAssembly);
        typeof(MicroKit.Persistence.ISpecificationEvaluator).Assembly.ShouldNotBe(AbstractionsAssembly);
    }

    [Fact]
    public void QueryOptions_IsInCore()
    {
        typeof(MicroKit.Persistence.QueryOptions<>).Assembly.ShouldBe(CoreAssembly);
    }

    [Fact]
    public void PagedResult_IsInCore()
    {
        typeof(MicroKit.Persistence.PagedResult<>).Assembly.ShouldBe(CoreAssembly);
    }

    // --- In EFCore (not Abstractions) ---

    [Fact]
    public void ITransactionalUnitOfWork_IsInEntityFrameworkCore_NotAbstractions()
    {
        // ADR-004: ITransactionalUnitOfWork is EF-specific and lives in EntityFrameworkCore, not Abstractions.
        typeof(MicroKit.Persistence.EntityFrameworkCore.ITransactionalUnitOfWork).Assembly.ShouldBe(EfCoreAssembly);
        typeof(MicroKit.Persistence.EntityFrameworkCore.ITransactionalUnitOfWork).Assembly.ShouldNotBe(AbstractionsAssembly);
    }

    [Fact]
    public void EfUnitOfWork_IsInEntityFrameworkCore()
    {
        typeof(MicroKit.Persistence.EntityFrameworkCore.EfUnitOfWork<>).Assembly.ShouldBe(EfCoreAssembly);
    }

    [Fact]
    public void EfSpecificationEvaluator_IsInEntityFrameworkCore()
    {
        typeof(MicroKit.Persistence.EntityFrameworkCore.EfSpecificationEvaluator).Assembly.ShouldBe(EfCoreAssembly);
    }

    // --- In Testing ---

    [Fact]
    public void InMemoryRepository_IsInTesting()
    {
        typeof(MicroKit.Persistence.Testing.InMemoryRepository<>).Assembly.ShouldBe(TestingAssembly);
    }
}
