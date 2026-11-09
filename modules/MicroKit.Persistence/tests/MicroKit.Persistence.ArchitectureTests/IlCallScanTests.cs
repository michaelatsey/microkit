using Microsoft.EntityFrameworkCore;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MicroKit.Persistence.ArchitectureTests;

/// <summary>
/// Scans the IL of every library assembly for calls that must never appear in library code, which
/// neither NetArchTest (type dependencies only) nor a consumer-side analyzer can see.
/// </summary>
public sealed class IlCallScanTests
{
    // Every runtime assembly of the module. The analyzers assembly references no EF Core.
    private static readonly Assembly[] LibraryAssemblies =
    [
        typeof(MicroKit.Persistence.Abstractions.IUnitOfWork).Assembly,
        typeof(MicroKit.Persistence.ISpecificationEvaluator).Assembly,
        typeof(MicroKit.Persistence.EntityFrameworkCore.ITransactionalUnitOfWork).Assembly,
        typeof(MicroKit.Persistence.EntityFrameworkCore.PostgreSql.PostgreSqlEfCoreBuilderExtensions).Assembly,
        typeof(MicroKit.Persistence.EntityFrameworkCore.SqlServer.SqlServerEfCoreBuilderExtensions).Assembly,
        typeof(MicroKit.Persistence.Specifications.QueryOptionsOrderingExtensions).Assembly,
        typeof(MicroKit.Persistence.Testing.InMemoryRepository<,>).Assembly,
    ];

    /// <summary>
    /// The parameterless <c>IgnoreQueryFilters()</c> disables every query filter of the model,
    /// including a tenant filter the library does not own (#196 D1). Only the named overload,
    /// <c>IgnoreQueryFilters(IReadOnlyCollection&lt;string&gt;)</c>, may ever be used — as
    /// <c>EfSpecificationEvaluator</c> does for <c>QueryOptions.IncludeDeleted</c>, with
    /// <c>SoftDelete.FilterName</c> only (#197 phase 4).
    /// </summary>
    [Fact]
    public void LibraryAssemblies_NeverCallParameterlessIgnoreQueryFilters()
    {
        var hits = LibraryAssemblies.SelectMany(a => FindParameterlessIgnoreQueryFilters(a.Location)).ToList();

        hits.ShouldBeEmpty(
            "library code must never disable every query filter of the model: " + string.Join("; ", hits));
    }

    /// <summary>
    /// Proves the scanner sees what the rule must catch: calls compiled into async state
    /// machines, lambda closures and expression trees, and that it lets the named overload pass.
    /// </summary>
    [Fact]
    public void Scanner_FindsCallsInAsyncMethodsLambdasAndExpressionTrees_AndIgnoresNamedOverload()
    {
        var hits = FindParameterlessIgnoreQueryFilters(typeof(IlCallScanTests).Assembly.Location)
            .Where(h => h.Contains(nameof(ScannerFixture), StringComparison.Ordinal))
            .ToList();

        hits.ShouldContain(h => h.Contains(nameof(ScannerFixture.DirectCall), StringComparison.Ordinal));
        hits.ShouldContain(h => h.Contains(nameof(ScannerFixture.AsyncCall), StringComparison.Ordinal));
        hits.ShouldContain(h => h.Contains(nameof(ScannerFixture.LambdaCall), StringComparison.Ordinal));
        hits.ShouldContain(h => h.Contains(nameof(ScannerFixture.ExpressionTreeCall), StringComparison.Ordinal));
        hits.ShouldNotContain(h => h.Contains(nameof(ScannerFixture.NamedOverloadCall), StringComparison.Ordinal));
    }

    private static IEnumerable<string> FindParameterlessIgnoreQueryFilters(string assemblyPath)
    {
        using var module = ModuleDefinition.ReadModule(assemblyPath);

        // GetTypes() flattens nested types, so compiler-generated state machines and closure
        // classes are scanned too.
        foreach (var type in module.GetTypes())
        {
            foreach (var method in type.Methods.Where(m => m.HasBody))
            {
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.OpCode.Code is not (Code.Call or Code.Callvirt or Code.Ldftn
                        or Code.Ldvirtftn or Code.Ldtoken))
                    {
                        continue;
                    }

                    if (instruction.Operand is MethodReference target && IsParameterlessIgnoreQueryFilters(target))
                        yield return $"{type.FullName}::{method.Name}";
                }
            }
        }
    }

    private static bool IsParameterlessIgnoreQueryFilters(MethodReference target)
    {
        var definition = target is GenericInstanceMethod generic ? generic.ElementMethod : target;
        return definition.Name == nameof(EntityFrameworkQueryableExtensions.IgnoreQueryFilters)
               && definition.DeclaringType.FullName == typeof(EntityFrameworkQueryableExtensions).FullName
               && definition.Parameters.Count == 1; // the IQueryable<T> source only
    }

    // Never executed: compiled only so the scanner has every call shape to find.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "IL fixture")]
    private sealed class ScannerFixture
    {
        public static IQueryable<Probe> DirectCall(IQueryable<Probe> q) => q.IgnoreQueryFilters();

        public static async Task<IQueryable<Probe>> AsyncCall(IQueryable<Probe> q)
        {
            await Task.Yield();
            return q.IgnoreQueryFilters();
        }

        public static Func<IQueryable<Probe>, IQueryable<Probe>> LambdaCall(string marker) =>
            q => marker.Length > 0 ? q.IgnoreQueryFilters() : q;

        public static System.Linq.Expressions.Expression<Func<IQueryable<Probe>, IQueryable<Probe>>> ExpressionTreeCall() =>
            q => q.IgnoreQueryFilters();

        public static IQueryable<Probe> NamedOverloadCall(IQueryable<Probe> q) => q.IgnoreQueryFilters(["SoftDelete"]);

        public sealed class Probe;
    }
}
