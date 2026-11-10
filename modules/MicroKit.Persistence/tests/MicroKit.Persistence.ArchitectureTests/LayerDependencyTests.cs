// GetReferencedAssemblies() checks direct references only (assembly manifest). Transitive refs are not checked.

namespace MicroKit.Persistence.ArchitectureTests;

public sealed class LayerDependencyTests
{
    private static readonly Assembly AbstractionsAssembly =
        typeof(MicroKit.Persistence.Abstractions.IUnitOfWork).Assembly;

    private static readonly Assembly CoreAssembly =
        typeof(MicroKit.Persistence.ISpecificationEvaluator).Assembly;

    private static readonly Assembly EfCoreAssembly =
        typeof(MicroKit.Persistence.EntityFrameworkCore.ITransactionalUnitOfWork).Assembly;

    private static readonly Assembly TestingAssembly =
        typeof(MicroKit.Persistence.Testing.InMemoryRepository<,>).Assembly;

    private static readonly Assembly SpecificationsAssembly =
        typeof(MicroKit.Persistence.Specifications.QueryOptionsOrderingExtensions).Assembly;

    private static readonly Assembly TestingPostgreSqlAssembly =
        typeof(MicroKit.Persistence.Testing.PostgreSql.PostgreSqlFixture).Assembly;

    private static readonly Assembly TestingSqlServerAssembly =
        typeof(MicroKit.Persistence.Testing.SqlServer.SqlServerFixture).Assembly;

    private static readonly Assembly PostgreSqlAssembly =
        typeof(MicroKit.Persistence.EntityFrameworkCore.PostgreSql.PostgreSqlEfCoreBuilderExtensions).Assembly;

    private static readonly Assembly SqlServerAssembly =
        typeof(MicroKit.Persistence.EntityFrameworkCore.SqlServer.SqlServerEfCoreBuilderExtensions).Assembly;

    // --- Abstractions assembly ---

    [Fact]
    public void Abstractions_DoesNotReferenceResult()
    {
        // #196 D17: the Result path lives in core (#197 phase 3), never in Abstractions.
        AbstractionsAssembly.GetReferencedAssemblies().Any(a => a.Name == "MicroKit.Result").ShouldBeFalse();
    }

    [Fact]
    public void Core_ReferencesResult()
    {
        CoreAssembly.GetReferencedAssemblies().Any(a => a.Name == "MicroKit.Result").ShouldBeTrue();
    }

    [Fact]
    public void Abstractions_DoesNotReferenceEntityFrameworkCore()
    {
        var refs = AbstractionsAssembly.GetReferencedAssemblies();
        refs.Any(a => a.Name!.Contains("Microsoft.EntityFrameworkCore")).ShouldBeFalse();
    }

    [Fact]
    public void Abstractions_DoesNotReferenceNpgsql()
    {
        var refs = AbstractionsAssembly.GetReferencedAssemblies();
        refs.Any(a => a.Name!.Contains("Npgsql")).ShouldBeFalse();
    }

    [Fact]
    public void Abstractions_DoesNotReferenceSqlServer()
    {
        var refs = AbstractionsAssembly.GetReferencedAssemblies();
        refs.Any(a => a.Name!.Contains("SqlServer")).ShouldBeFalse();
    }

    [Fact]
    public void Abstractions_DoesNotReferencePersistenceCore()
    {
        var refs = AbstractionsAssembly.GetReferencedAssemblies();
        refs.Any(a => a.Name == "MicroKit.Persistence").ShouldBeFalse();
    }

    [Fact]
    public void Abstractions_DoesNotReferencePersistenceEntityFrameworkCore()
    {
        var refs = AbstractionsAssembly.GetReferencedAssemblies();
        refs.Any(a => a.Name!.Contains("MicroKit.Persistence.EntityFrameworkCore")).ShouldBeFalse();
    }

    // --- Core assembly ---

    [Fact]
    public void Core_DoesNotReferenceEntityFrameworkCore()
    {
        var refs = CoreAssembly.GetReferencedAssemblies();
        refs.Any(a => a.Name!.Contains("Microsoft.EntityFrameworkCore")).ShouldBeFalse();
    }

    [Fact]
    public void Core_DoesNotReferenceNpgsql()
    {
        var refs = CoreAssembly.GetReferencedAssemblies();
        refs.Any(a => a.Name!.Contains("Npgsql")).ShouldBeFalse();
    }

    [Fact]
    public void Core_DoesNotReferenceEntityFrameworkCoreProject()
    {
        var refs = CoreAssembly.GetReferencedAssemblies();
        refs.Any(a => a.Name == "MicroKit.Persistence.EntityFrameworkCore").ShouldBeFalse();
    }

    [Fact]
    public void Core_DoesNotReference_MediatRContracts()
    {
        var refs = CoreAssembly.GetReferencedAssemblies();
        refs.Any(a => a.Name!.Contains("MediatR.Contracts")).ShouldBeFalse();
    }

    // --- EntityFrameworkCore assembly ---

    [Fact]
    public void EntityFrameworkCore_DoesNotReferenceNpgsql()
    {
        var refs = EfCoreAssembly.GetReferencedAssemblies();
        refs.Any(a => a.Name!.Contains("Npgsql")).ShouldBeFalse();
    }

    [Fact]
    public void EntityFrameworkCore_DoesNotReferenceSqlServer()
    {
        var refs = EfCoreAssembly.GetReferencedAssemblies();
        refs.Any(a => a.Name!.Contains("SqlServer")).ShouldBeFalse();
    }

    [Fact]
    public void EntityFrameworkCore_DoesNotReference_MediatRContracts()
    {
        var refs = EfCoreAssembly.GetReferencedAssemblies();
        refs.Any(a => a.Name!.Contains("MediatR.Contracts")).ShouldBeFalse();
    }

    // --- Testing assembly ---

    [Fact]
    public void Testing_DoesNotReferenceEntityFrameworkCore()
    {
        var refs = TestingAssembly.GetReferencedAssemblies();
        refs.Any(a => a.Name!.Contains("Microsoft.EntityFrameworkCore")).ShouldBeFalse();
    }

    [Fact]
    public void Testing_DoesNotReferenceNpgsql()
    {
        var refs = TestingAssembly.GetReferencedAssemblies();
        refs.Any(a => a.Name!.Contains("Npgsql")).ShouldBeFalse();
    }

    [Fact]
    public void Testing_DoesNotReference_MediatRContracts()
    {
        var refs = TestingAssembly.GetReferencedAssemblies();
        refs.Any(a => a.Name!.Contains("MediatR.Contracts")).ShouldBeFalse();
    }

    [Fact]
    public void Testing_DoesNotReferenceTestcontainers()
    {
        // #197 phase 7: the in-memory doubles and the Docker probe take no Testcontainers dependency.
        var refs = TestingAssembly.GetReferencedAssemblies();
        refs.Any(a => a.Name!.Contains("Testcontainers")).ShouldBeFalse();
    }

    // --- Relational fixture packages (#197 phase 7, ADR-015) ---

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("MicroKit.Persistence.EntityFrameworkCore")]
    [InlineData("Npgsql")]
    [InlineData("Microsoft.Data.SqlClient")]
    [InlineData("xunit")]
    [InlineData("nunit")]
    [InlineData("MSTest")]
    [InlineData("Microsoft.VisualStudio.TestPlatform.TestFramework")]
    public void TheFixturePackages_DoNotReference(string prefix)
    {
        foreach (var assembly in new[] { TestingPostgreSqlAssembly, TestingSqlServerAssembly })
        {
            assembly.GetReferencedAssemblies()
                .Any(a => a.Name!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ShouldBeFalse($"{assembly.GetName().Name} references {prefix}");
        }
    }

    [Fact]
    public void TheFixturePackages_DoNotReferenceEachOther()
    {
        TestingPostgreSqlAssembly.GetReferencedAssemblies().Any(a => a.Name == "MicroKit.Persistence.Testing.SqlServer").ShouldBeFalse();
        TestingSqlServerAssembly.GetReferencedAssemblies().Any(a => a.Name == "MicroKit.Persistence.Testing.PostgreSql").ShouldBeFalse();
    }

    [Fact]
    public void TheFixturePackages_ReferenceTesting_AndTheirTestcontainersModule()
    {
        TestingPostgreSqlAssembly.GetReferencedAssemblies().Any(a => a.Name == "MicroKit.Persistence.Testing").ShouldBeTrue();
        TestingSqlServerAssembly.GetReferencedAssemblies().Any(a => a.Name == "MicroKit.Persistence.Testing").ShouldBeTrue();
        TestingPostgreSqlAssembly.GetReferencedAssemblies().Any(a => a.Name == "Testcontainers.PostgreSql").ShouldBeTrue();
        TestingSqlServerAssembly.GetReferencedAssemblies().Any(a => a.Name == "Testcontainers.MsSql").ShouldBeTrue();
    }

    // --- Sibling isolation ---

    [Fact]
    public void Specifications_DoesNotReferenceTesting()
    {
        var refs = SpecificationsAssembly.GetReferencedAssemblies();
        refs.Any(a => a.Name == "MicroKit.Persistence.Testing").ShouldBeFalse();
    }

    [Fact]
    public void Specifications_AndTheFixturePackages_DoNotReferenceEachOther()
    {
        SpecificationsAssembly.GetReferencedAssemblies()
            .Any(a => a.Name!.StartsWith("MicroKit.Persistence.Testing.", StringComparison.Ordinal)).ShouldBeFalse();
        TestingPostgreSqlAssembly.GetReferencedAssemblies().Any(a => a.Name == "MicroKit.Persistence.Specifications").ShouldBeFalse();
        TestingSqlServerAssembly.GetReferencedAssemblies().Any(a => a.Name == "MicroKit.Persistence.Specifications").ShouldBeFalse();
    }

    [Fact]
    public void Testing_DoesNotReferenceSpecifications()
    {
        var refs = TestingAssembly.GetReferencedAssemblies();
        refs.Any(a => a.Name == "MicroKit.Persistence.Specifications").ShouldBeFalse();
    }

    // --- Provider assembly dependency checks ---

    [Fact]
    public void PostgreSql_References_Npgsql()
    {
        var refs = PostgreSqlAssembly.GetReferencedAssemblies();
        refs.Any(a => a.Name!.Contains("Npgsql")).ShouldBeTrue();
    }

    [Fact]
    public void SqlServer_References_SqlServer()
    {
        var refs = SqlServerAssembly.GetReferencedAssemblies();
        refs.Any(a => a.Name!.Contains("SqlServer")).ShouldBeTrue();
    }
}
