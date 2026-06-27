using MicroKit.Tenancy.Analyzers.Tests.Stubs;

namespace MicroKit.Tenancy.Analyzers.Tests;

public sealed class QueryFilterBypassAnalyzerTests
{
    // -------------------------------------------------------------------------
    // MKT002 — IgnoreQueryFilters() called without [MTK-BYPASS] comment
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MKT002_NoComment_RaisesWarning()
    {
        var source = MultitenancyStubs.All + """
            namespace MyApp.Infrastructure
            {
                class OrderEntity : ITenantEntity { public TenantId TenantId { get; } = default!; }

                class OrderRepository
                {
                    void GetAllOrders(IQueryable<OrderEntity> query)
                    {
                        var result = query.{|MKT002:IgnoreQueryFilters|}();
                    }
                }
            }
            """;

        await new CSharpAnalyzerTest<QueryFilterBypassAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task MKT002_UnrelatedComment_RaisesWarning()
    {
        var source = MultitenancyStubs.All + """
            namespace MyApp.Infrastructure
            {
                class OrderEntity : ITenantEntity { public TenantId TenantId { get; } = default!; }

                class OrderRepository
                {
                    void GetAllOrders(IQueryable<OrderEntity> query)
                    {
                        // This query loads orders across all tenants
                        var result = query.{|MKT002:IgnoreQueryFilters|}();
                    }
                }
            }
            """;

        await new CSharpAnalyzerTest<QueryFilterBypassAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    // -------------------------------------------------------------------------
    // Negative tests — should NOT raise diagnostics
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MKT002_SameLineBypassComment_NoDiagnostic()
    {
        var source = MultitenancyStubs.All + """
            namespace MyApp.Infrastructure
            {
                class OrderEntity : ITenantEntity { public TenantId TenantId { get; } = default!; }

                class OrderRepository
                {
                    void GetAllOrders(IQueryable<OrderEntity> query)
                    {
                        var result = query.IgnoreQueryFilters(); // [MTK-BYPASS] Admin billing report across all tenants
                    }
                }
            }
            """;

        await new CSharpAnalyzerTest<QueryFilterBypassAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task MKT002_PrecedingLineBypassComment_NoDiagnostic()
    {
        var source = MultitenancyStubs.All + """
            namespace MyApp.Infrastructure
            {
                class OrderEntity : ITenantEntity { public TenantId TenantId { get; } = default!; }

                class OrderRepository
                {
                    void GetAllOrders(IQueryable<OrderEntity> query)
                    {
                        // [MTK-BYPASS] Cross-tenant admin report for billing
                        var result = query.IgnoreQueryFilters();
                    }
                }
            }
            """;

        await new CSharpAnalyzerTest<QueryFilterBypassAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }
}
