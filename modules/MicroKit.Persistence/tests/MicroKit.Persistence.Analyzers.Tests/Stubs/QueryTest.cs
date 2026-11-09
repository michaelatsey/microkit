using Microsoft.CodeAnalysis.Diagnostics;

namespace MicroKit.Persistence.Analyzers.Tests.Stubs;

/// <summary>Which stub documents a query-shape test compiles beside its code.</summary>
[Flags]
internal enum StubSet
{
    None = 0,
    MicroKit = 1,
    EntityFrameworkCore = 2,
    Relational = 4,
    Shop = 8,
    All = MicroKit | EntityFrameworkCore | Relational | Shop,
}

/// <summary>
/// Runs a query-shape analyzer over <paramref name="code"/> plus the chosen stub documents, against the
/// .NET 9 reference assemblies (records, <c>init</c>, <c>DateOnly</c>, <c>NotMappedAttribute</c>).
/// </summary>
internal static class QueryTest<TAnalyzer>
    where TAnalyzer : DiagnosticAnalyzer, new()
{
    public static Task RunAsync(string code, StubSet stubs = StubSet.All, bool usings = true)
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, CompatXUnitVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        test.TestState.Sources.Add(("Test.cs", usings ? QueryStubs.Usings + code : code));
        if (stubs.HasFlag(StubSet.MicroKit))
            test.TestState.Sources.Add(("MicroKit.cs", QueryStubs.MicroKit));
        if (stubs.HasFlag(StubSet.EntityFrameworkCore))
            test.TestState.Sources.Add(("EntityFrameworkCore.cs", QueryStubs.EntityFrameworkCore));
        if (stubs.HasFlag(StubSet.Relational))
            test.TestState.Sources.Add(("Relational.cs", QueryStubs.Relational));
        if (stubs.HasFlag(StubSet.Shop))
            test.TestState.Sources.Add(("Shop.cs", QueryStubs.Shop));
        if (stubs.HasFlag(StubSet.Shop) && stubs.HasFlag(StubSet.EntityFrameworkCore))
            test.TestState.Sources.Add(("ShopContext.cs", QueryStubs.ShopContext));
        return test.RunAsync();
    }
}
