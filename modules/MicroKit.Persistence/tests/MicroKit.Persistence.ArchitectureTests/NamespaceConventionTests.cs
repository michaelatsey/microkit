// All Abstractions types use the flat root namespace — ResideInNamespace behaves as exact match here.

namespace MicroKit.Persistence.ArchitectureTests;

public sealed class NamespaceConventionTests
{
    private static readonly Assembly AbstractionsAssembly =
        typeof(MicroKit.Persistence.Abstractions.IUnitOfWork).Assembly;

    private static readonly Assembly CoreAssembly =
        typeof(MicroKit.Persistence.ISpecificationEvaluator).Assembly;

    private static readonly Assembly EfCoreAssembly =
        typeof(MicroKit.Persistence.EntityFrameworkCore.ITransactionalUnitOfWork).Assembly;

    private static readonly Assembly TestingAssembly =
        typeof(MicroKit.Persistence.Testing.InMemoryRepository<>).Assembly;

    private static readonly Assembly SpecificationsAssembly =
        typeof(MicroKit.Persistence.Specifications.QueryOptionsOrderingExtensions).Assembly;

    private static readonly Assembly PostgreSqlAssembly =
        typeof(MicroKit.Persistence.EntityFrameworkCore.PostgreSql.PostgreSqlEfCoreBuilderExtensions).Assembly;

    private static readonly Assembly SqlServerAssembly =
        typeof(MicroKit.Persistence.EntityFrameworkCore.SqlServer.SqlServerEfCoreBuilderExtensions).Assembly;

    [Fact]
    public void AbstractionsPublicTypes_ResideInCorrectNamespace()
    {
        var result = Types.InAssembly(AbstractionsAssembly)
            .That().ArePublic()
            .Should().ResideInNamespace("MicroKit.Persistence.Abstractions")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(result.FailingTypeNames?.ToString() ?? string.Empty);
    }

    [Fact]
    public void CorePublicTypes_ResideInCorrectNamespace()
    {
        var result = Types.InAssembly(CoreAssembly)
            .That().ArePublic()
            .Should().ResideInNamespace("MicroKit.Persistence")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(result.FailingTypeNames?.ToString() ?? string.Empty);
    }

    [Fact]
    public void EntityFrameworkCorePublicTypes_ResideInCorrectNamespace()
    {
        var result = Types.InAssembly(EfCoreAssembly)
            .That().ArePublic()
            .Should().ResideInNamespace("MicroKit.Persistence.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(result.FailingTypeNames?.ToString() ?? string.Empty);
    }

    [Fact]
    public void TestingPublicTypes_ResideInCorrectNamespace()
    {
        var result = Types.InAssembly(TestingAssembly)
            .That().ArePublic()
            .Should().ResideInNamespace("MicroKit.Persistence.Testing")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(result.FailingTypeNames?.ToString() ?? string.Empty);
    }

    [Fact]
    public void SpecificationsPublicTypes_ResideInCorrectNamespace()
    {
        var result = Types.InAssembly(SpecificationsAssembly)
            .That().ArePublic()
            .Should().ResideInNamespace("MicroKit.Persistence.Specifications")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(result.FailingTypeNames?.ToString() ?? string.Empty);
    }

    [Fact]
    public void PostgreSqlPublicTypes_ResideInCorrectNamespace()
    {
        var result = Types.InAssembly(PostgreSqlAssembly)
            .That().ArePublic()
            .Should().ResideInNamespace("MicroKit.Persistence.EntityFrameworkCore.PostgreSql")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(result.FailingTypeNames?.ToString() ?? string.Empty);
    }

    [Fact]
    public void SqlServerPublicTypes_ResideInCorrectNamespace()
    {
        var result = Types.InAssembly(SqlServerAssembly)
            .That().ArePublic()
            .Should().ResideInNamespace("MicroKit.Persistence.EntityFrameworkCore.SqlServer")
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
    public void ExtensionClasses_InSpecificationsAssembly_AreStatic()
    {
        var result = Types.InAssembly(SpecificationsAssembly)
            .That().HaveNameEndingWith("Extensions")
            .Should().BeStatic()
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(result.FailingTypeNames?.ToString() ?? string.Empty);
    }
}
