using MicroKit.Tenancy.Analyzers.Tests.Stubs;

namespace MicroKit.Tenancy.Analyzers.Tests;

public sealed class TenantEntityAnalyzerTests
{
    // -------------------------------------------------------------------------
    // MKT001 — Entity implementing ITenantEntity without TenantId property
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MKT001_NoTenantIdProperty_RaisesError()
    {
        var source = MultitenancyStubs.All + """
            namespace MyApp.Domain
            {
                class {|MKT001:Order|} : ITenantEntity
                {
                    public int Id { get; set; }
                }
            }
            """;

        // CS0535 is expected here: the class intentionally omits the TenantId implementation
        // to test that MKT001 fires. We suppress compiler diagnostics so the test only
        // verifies the analyzer output.
        await new CSharpAnalyzerTest<TenantEntityAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
            CompilerDiagnostics = CompilerDiagnostics.None,
        }.RunAsync();
    }

    [Fact]
    public async Task MKT001_NullableTenantIdProperty_RaisesError()
    {
        var source = MultitenancyStubs.All + """
            namespace MyApp.Domain
            {
                class {|MKT001:Invoice|} : ITenantEntity
                {
                    public TenantId? TenantId { get; set; }
                }
            }
            """;

        await new CSharpAnalyzerTest<TenantEntityAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    // -------------------------------------------------------------------------
    // Negative tests — should NOT raise diagnostics
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MKT001_NonNullableTenantIdProperty_NoDiagnostic()
    {
        var source = MultitenancyStubs.All + """
            namespace MyApp.Domain
            {
                class Order : ITenantEntity
                {
                    public TenantId TenantId { get; private set; } = default!;
                }
            }
            """;

        await new CSharpAnalyzerTest<TenantEntityAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    // Architect issue #5: explicit test for inherited TenantId from a base class
    [Fact]
    public async Task MKT001_InheritedNonNullableTenantId_NoDiagnostic()
    {
        var source = MultitenancyStubs.All + """
            namespace MyApp.Domain
            {
                abstract class TenantEntityBase : ITenantEntity
                {
                    public TenantId TenantId { get; protected set; } = default!;
                }

                class Order : TenantEntityBase
                {
                    public int Quantity { get; set; }
                }
            }
            """;

        await new CSharpAnalyzerTest<TenantEntityAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task MKT001_DoesNotImplementITenantEntity_NoDiagnostic()
    {
        var source = MultitenancyStubs.All + """
            namespace MyApp.Domain
            {
                class Product
                {
                    public int Id { get; set; }
                }
            }
            """;

        await new CSharpAnalyzerTest<TenantEntityAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task MKT001_AbstractClass_NoDiagnostic()
    {
        var source = MultitenancyStubs.All + """
            namespace MyApp.Domain
            {
                abstract class AbstractEntity : ITenantEntity
                {
                    public abstract TenantId TenantId { get; }
                }
            }
            """;

        await new CSharpAnalyzerTest<TenantEntityAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }
}
