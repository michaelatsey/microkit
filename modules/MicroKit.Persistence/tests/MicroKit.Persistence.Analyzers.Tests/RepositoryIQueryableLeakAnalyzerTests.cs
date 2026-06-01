using MicroKit.Persistence.Analyzers.Tests.Stubs;

namespace MicroKit.Persistence.Analyzers.Tests;

public sealed class RepositoryIQueryableLeakAnalyzerTests
{
    // -------------------------------------------------------------------------
    // MKP005 — IQueryable<T> return type on repository method
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MKP005_IQueryableReturn_OnReadRepoImpl_RaisesError()
    {
        var source = PersistenceStubs.All + """
            class MyAggregate : IAggregateRoot { }

            class MyReadRepo : IReadRepository<MyAggregate>
            {
                public IQueryable<MyAggregate> {|MKP005:GetAll|}() => throw new System.NotImplementedException();
            }
            """;

        await new CSharpAnalyzerTest<RepositoryIQueryableLeakAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task MKP005_IQueryableReturn_OnWriteRepoImpl_RaisesError()
    {
        var source = PersistenceStubs.All + """
            class MyAggregate : IAggregateRoot { }

            class MyWriteRepo : IRepository<MyAggregate>
            {
                public ValueTask AddAsync(MyAggregate a, CancellationToken ct = default) => default;
                public ValueTask UpdateAsync(MyAggregate a, CancellationToken ct = default) => default;
                public ValueTask DeleteAsync(MyAggregate a, CancellationToken ct = default) => default;
                public ValueTask CommitAsync(CancellationToken ct = default) => default;

                public IQueryable<MyAggregate> {|MKP005:Query|}() => throw new System.NotImplementedException();
            }
            """;

        await new CSharpAnalyzerTest<RepositoryIQueryableLeakAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task MKP005_TaskOfIQueryableReturn_OnReadRepoImpl_RaisesError()
    {
        var source = PersistenceStubs.All + """
            class MyAggregate : IAggregateRoot { }

            class MyReadRepo : IReadRepository<MyAggregate>
            {
                public Task<IQueryable<MyAggregate>> {|MKP005:GetQueryAsync|}(CancellationToken ct = default)
                    => throw new System.NotImplementedException();
            }
            """;

        await new CSharpAnalyzerTest<RepositoryIQueryableLeakAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    // -------------------------------------------------------------------------
    // Negative tests — should NOT raise diagnostics
    // -------------------------------------------------------------------------

    [Fact]
    public async Task IReadOnlyListReturn_OnReadRepoImpl_NoDiagnostic()
    {
        var source = PersistenceStubs.All + """
            class MyAggregate : IAggregateRoot { }

            class MyReadRepo : MicroKit.Persistence.IReadRepository<MyAggregate>
            {
                public ValueTask<IReadOnlyList<MyAggregate>> ListAsync(CancellationToken ct = default)
                    => new ValueTask<IReadOnlyList<MyAggregate>>(new List<MyAggregate>());
            }
            """;

        await new CSharpAnalyzerTest<RepositoryIQueryableLeakAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task IQueryableReturn_OnArbitraryClass_NoDiagnostic()
    {
        var source = PersistenceStubs.All + """
            class ReportBuilder
            {
                // Not a repository — must not be flagged
                public IQueryable<string> GetItems() => new List<string>().AsQueryable();
            }
            """;

        await new CSharpAnalyzerTest<RepositoryIQueryableLeakAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task PrivateIQueryableMethod_OnReadRepoImpl_NoDiagnostic()
    {
        var source = PersistenceStubs.All + """
            class MyAggregate : IAggregateRoot { }

            class MyReadRepo : IReadRepository<MyAggregate>
            {
                // Private helper — not part of the public surface; must not be flagged
                private IQueryable<MyAggregate> BuildQuery() => new List<MyAggregate>().AsQueryable();
            }
            """;

        await new CSharpAnalyzerTest<RepositoryIQueryableLeakAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }
}
