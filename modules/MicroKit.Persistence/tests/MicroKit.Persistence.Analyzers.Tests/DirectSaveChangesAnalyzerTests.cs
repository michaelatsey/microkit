using MicroKit.Persistence.Analyzers.Tests.Stubs;

namespace MicroKit.Persistence.Analyzers.Tests;

public sealed class DirectSaveChangesAnalyzerTests
{
    // -------------------------------------------------------------------------
    // MKP003 — SaveChangesAsync / SaveChanges called on DbContext outside UoW
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MKP003_SaveChangesAsync_InHandler_RaisesWarning()
    {
        var source = PersistenceStubs.All + """
            class AppDbContext : DbContext { }

            class CreateOrderHandler
            {
                private readonly AppDbContext _ctx;
                public CreateOrderHandler(AppDbContext ctx) => _ctx = ctx;

                public async Task HandleAsync(CancellationToken ct)
                {
                    await {|MKP003:_ctx.SaveChangesAsync(ct)|};
                }
            }
            """;

        await new CSharpAnalyzerTest<DirectSaveChangesAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task MKP003_SaveChanges_InHandler_RaisesWarning()
    {
        var source = PersistenceStubs.All + """
            class AppDbContext : DbContext { }

            class SyncHandler
            {
                private readonly AppDbContext _ctx;
                public SyncHandler(AppDbContext ctx) => _ctx = ctx;

                public void Handle()
                {
                    {|MKP003:_ctx.SaveChanges()|};
                }
            }
            """;

        await new CSharpAnalyzerTest<DirectSaveChangesAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    // -------------------------------------------------------------------------
    // Negative tests — should NOT raise diagnostics
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SaveChangesAsync_InUnitOfWorkImpl_NoDiagnostic()
    {
        var source = PersistenceStubs.All + """
            class AppDbContext : DbContext { }

            // Legitimate IUnitOfWork implementation — SaveChangesAsync is allowed here
            class EfUoW : IUnitOfWork
            {
                private readonly AppDbContext _ctx;
                public EfUoW(AppDbContext ctx) => _ctx = ctx;

                public async ValueTask CommitAsync(CancellationToken ct = default)
                {
                    await _ctx.SaveChangesAsync(ct);
                }
            }
            """;

        await new CSharpAnalyzerTest<DirectSaveChangesAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task SaveChangesAsync_InTransactionalUoWImpl_NoDiagnostic()
    {
        var source = PersistenceStubs.All + """
            class AppDbContext : DbContext { }

            class EfTransactionalUoW : ITransactionalUnitOfWork
            {
                private readonly AppDbContext _ctx;
                public EfTransactionalUoW(AppDbContext ctx) => _ctx = ctx;

                public async ValueTask CommitAsync(CancellationToken ct = default)
                {
                    await _ctx.SaveChangesAsync(ct);
                }
            }
            """;

        await new CSharpAnalyzerTest<DirectSaveChangesAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task CommitAsync_ViaIUnitOfWork_NoDiagnostic()
    {
        var source = PersistenceStubs.All + """
            class CreateOrderHandler
            {
                private readonly IUnitOfWork _uow;
                public CreateOrderHandler(IUnitOfWork uow) => _uow = uow;

                public async Task HandleAsync(CancellationToken ct)
                {
                    await _uow.CommitAsync(ct);
                }
            }
            """;

        await new CSharpAnalyzerTest<DirectSaveChangesAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }
}
