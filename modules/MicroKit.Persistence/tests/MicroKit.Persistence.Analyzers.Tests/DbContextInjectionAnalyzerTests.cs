using MicroKit.Persistence.Analyzers.Tests.Stubs;

namespace MicroKit.Persistence.Analyzers.Tests;

public sealed class DbContextInjectionAnalyzerTests
{
    // -------------------------------------------------------------------------
    // MKP004 — DbContext injected outside infrastructure layer
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MKP004_DbContextParam_InApplicationHandler_RaisesWarning()
    {
        var source = PersistenceStubs.All + """
            namespace MyApp.Application.Orders
            {
                class AppDbContext : DbContext { }

                class CreateOrderHandler
                {
                    public CreateOrderHandler({|MKP004:AppDbContext|} ctx) { }
                }
            }
            """;

        await new CSharpAnalyzerTest<DbContextInjectionAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task MKP004_DbContextParam_InDomainService_RaisesWarning()
    {
        var source = PersistenceStubs.All + """
            namespace MyApp.Domain.Services
            {
                class AppDbContext : DbContext { }

                class OrderDomainService
                {
                    public OrderDomainService({|MKP004:AppDbContext|} ctx) { }
                }
            }
            """;

        await new CSharpAnalyzerTest<DbContextInjectionAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    // -------------------------------------------------------------------------
    // Negative tests — should NOT raise diagnostics
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DbContextParam_InInfrastructureNamespace_NoDiagnostic()
    {
        var source = PersistenceStubs.All + """
            namespace MyApp.Infrastructure.Repositories
            {
                class AppDbContext : DbContext { }

                class EfOrderRepository
                {
                    public EfOrderRepository(AppDbContext ctx) { }
                }
            }
            """;

        await new CSharpAnalyzerTest<DbContextInjectionAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task DbContextParam_InEntityFrameworkCoreNamespace_NoDiagnostic()
    {
        var source = PersistenceStubs.All + """
            namespace MyApp.EntityFrameworkCore
            {
                class AppDbContext : DbContext { }

                class EfUnitOfWork
                {
                    public EfUnitOfWork(AppDbContext ctx) { }
                }
            }
            """;

        await new CSharpAnalyzerTest<DbContextInjectionAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task DbContextParam_InPersistenceNamespace_NoDiagnostic()
    {
        var source = PersistenceStubs.All + """
            namespace MyApp.Persistence
            {
                class AppDbContext : DbContext { }

                class AppDbContextFactory
                {
                    public AppDbContextFactory(AppDbContext ctx) { }
                }
            }
            """;

        await new CSharpAnalyzerTest<DbContextInjectionAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task NonDbContextParam_InHandler_NoDiagnostic()
    {
        var source = PersistenceStubs.All + """
            class MyAggregate : IAggregateRoot { }

            namespace MyApp.Application
            {
                class CreateOrderHandler
                {
                    public CreateOrderHandler(IRepository<MyAggregate> repo) { }
                }
            }
            """;

        await new CSharpAnalyzerTest<DbContextInjectionAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }
}
