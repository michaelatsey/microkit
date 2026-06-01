using MicroKit.Persistence.Analyzers.Tests.Stubs;

namespace MicroKit.Persistence.Analyzers.Tests;

public sealed class ReadRepositoryMutationAnalyzerTests
{
    // -------------------------------------------------------------------------
    // MKP001 — CommitAsync / SaveChangesAsync inside read repo body
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MKP001_SaveChangesAsync_InReadRepoImpl_RaisesError()
    {
        var source = PersistenceStubs.All + """
            class MyAggregate : IAggregateRoot { }

            class MyReadRepo : IReadRepository<MyAggregate>
            {
                private readonly DbContext _ctx;
                public MyReadRepo(DbContext ctx) => _ctx = ctx;

                public async Task LoadAsync(CancellationToken ct)
                {
                    await {|MKP001:_ctx.SaveChangesAsync(ct)|};
                }
            }
            """;

        await new CSharpAnalyzerTest<ReadRepositoryMutationAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task MKP001_CommitAsync_InReadRepoImpl_RaisesError()
    {
        var source = PersistenceStubs.All + """
            class MyAggregate : IAggregateRoot { }

            class MyReadRepo : IReadRepository<MyAggregate>
            {
                private readonly IUnitOfWork _uow;
                public MyReadRepo(IUnitOfWork uow) => _uow = uow;

                public async Task DoWorkAsync(CancellationToken ct)
                {
                    await {|MKP001:_uow.CommitAsync(ct)|};
                }
            }
            """;

        await new CSharpAnalyzerTest<ReadRepositoryMutationAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    // -------------------------------------------------------------------------
    // MKP002 — Write method invoked inside read repo body
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MKP002_AddAsync_InvocationInReadRepoImpl_RaisesError()
    {
        var source = PersistenceStubs.All + """
            class MyAggregate : IAggregateRoot { }

            class MyReadRepo : IReadRepository<MyAggregate>
            {
                private readonly IRepository<MyAggregate> _repo;
                public MyReadRepo(IRepository<MyAggregate> repo) => _repo = repo;

                public async Task DoWorkAsync(MyAggregate agg, CancellationToken ct)
                {
                    await {|MKP002:_repo.AddAsync(agg, ct)|};
                }
            }
            """;

        await new CSharpAnalyzerTest<ReadRepositoryMutationAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task MKP002_UpdateAsync_InvocationInReadRepoImpl_RaisesError()
    {
        var source = PersistenceStubs.All + """
            class MyAggregate : IAggregateRoot { }

            class MyReadRepo : IReadRepository<MyAggregate>
            {
                private readonly IRepository<MyAggregate> _repo;
                public MyReadRepo(IRepository<MyAggregate> repo) => _repo = repo;

                public async Task DoWorkAsync(MyAggregate agg, CancellationToken ct)
                {
                    await {|MKP002:_repo.UpdateAsync(agg, ct)|};
                }
            }
            """;

        await new CSharpAnalyzerTest<ReadRepositoryMutationAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task MKP002_DeleteAsync_InvocationInReadRepoImpl_RaisesError()
    {
        var source = PersistenceStubs.All + """
            class MyAggregate : IAggregateRoot { }

            class MyReadRepo : IReadRepository<MyAggregate>
            {
                private readonly IRepository<MyAggregate> _repo;
                public MyReadRepo(IRepository<MyAggregate> repo) => _repo = repo;

                public async Task DoWorkAsync(MyAggregate agg, CancellationToken ct)
                {
                    await {|MKP002:_repo.DeleteAsync(agg, ct)|};
                }
            }
            """;

        await new CSharpAnalyzerTest<ReadRepositoryMutationAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    // -------------------------------------------------------------------------
    // MKP002 — Write method declared on read repo implementation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MKP002_AddAsync_DeclaredOnReadRepoImpl_RaisesError()
    {
        var source = PersistenceStubs.All + """
            class MyAggregate : IAggregateRoot { }

            class MyReadRepo : IReadRepository<MyAggregate>
            {
                public ValueTask {|MKP002:AddAsync|}(MyAggregate agg, CancellationToken ct = default)
                    => default;
            }
            """;

        await new CSharpAnalyzerTest<ReadRepositoryMutationAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    // -------------------------------------------------------------------------
    // Negative tests — should NOT raise diagnostics
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CommitAsync_InWriteRepoImpl_NoDiagnostic()
    {
        var source = PersistenceStubs.All + """
            class MyAggregate : IAggregateRoot { }

            class MyWriteRepo : IRepository<MyAggregate>
            {
                public ValueTask AddAsync(MyAggregate a, CancellationToken ct = default) => default;
                public ValueTask UpdateAsync(MyAggregate a, CancellationToken ct = default) => default;
                public ValueTask DeleteAsync(MyAggregate a, CancellationToken ct = default) => default;
                public ValueTask CommitAsync(CancellationToken ct = default) => default;
            }
            """;

        await new CSharpAnalyzerTest<ReadRepositoryMutationAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task CommitAsync_InArbitraryClass_NoDiagnostic()
    {
        var source = PersistenceStubs.All + """
            class MyService
            {
                private readonly IUnitOfWork _uow;
                public MyService(IUnitOfWork uow) => _uow = uow;

                public async Task HandleAsync(CancellationToken ct)
                {
                    await _uow.CommitAsync(ct);
                }
            }
            """;

        await new CSharpAnalyzerTest<ReadRepositoryMutationAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task ListAsync_InReadRepoImpl_NoDiagnostic()
    {
        var source = PersistenceStubs.All + """
            class MyAggregate : IAggregateRoot { }

            class MyReadRepo : MicroKit.Persistence.IReadRepository<MyAggregate>
            {
                public ValueTask<IReadOnlyList<MyAggregate>> ListAsync(CancellationToken ct = default)
                    => new ValueTask<IReadOnlyList<MyAggregate>>(new List<MyAggregate>());
            }
            """;

        await new CSharpAnalyzerTest<ReadRepositoryMutationAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }
}
