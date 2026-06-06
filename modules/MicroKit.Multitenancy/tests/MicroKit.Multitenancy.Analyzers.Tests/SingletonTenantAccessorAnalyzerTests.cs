using MicroKit.Multitenancy.Analyzers.Tests.Stubs;

namespace MicroKit.Multitenancy.Analyzers.Tests;

public sealed class SingletonTenantAccessorAnalyzerTests
{
    // -------------------------------------------------------------------------
    // MKT003 — ITenantContextAccessor injected in a singleton
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MKT003_TwoArgAddSingleton_WithAccessorInCtor_RaisesError()
    {
        var source = MultitenancyStubs.All + """
            namespace MyApp
            {
                interface IMyService { }

                class MyService : IMyService
                {
                    public MyService(ITenantContextAccessor accessor) { }
                }

                class Startup
                {
                    void Register(IServiceCollection services)
                    {
                        {|MKT003:services.AddSingleton<IMyService, MyService>()|};
                    }
                }
            }
            """;

        await new CSharpAnalyzerTest<SingletonTenantAccessorAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task MKT003_OneArgAddSingleton_WithAccessorInCtor_RaisesError()
    {
        var source = MultitenancyStubs.All + """
            namespace MyApp
            {
                class MyBackgroundService
                {
                    public MyBackgroundService(ITenantContextAccessor accessor) { }
                }

                class Startup
                {
                    void Register(IServiceCollection services)
                    {
                        {|MKT003:services.AddSingleton<MyBackgroundService>()|};
                    }
                }
            }
            """;

        await new CSharpAnalyzerTest<SingletonTenantAccessorAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    // -------------------------------------------------------------------------
    // Negative tests — should NOT raise diagnostics
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MKT003_AddScoped_WithAccessorInCtor_NoDiagnostic()
    {
        var source = MultitenancyStubs.All + """
            namespace MyApp
            {
                interface IMyService { }

                class MyService : IMyService
                {
                    public MyService(ITenantContextAccessor accessor) { }
                }

                class Startup
                {
                    void Register(IServiceCollection services)
                    {
                        services.AddScoped<IMyService, MyService>();
                    }
                }
            }
            """;

        await new CSharpAnalyzerTest<SingletonTenantAccessorAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task MKT003_AddTransient_WithAccessorInCtor_NoDiagnostic()
    {
        var source = MultitenancyStubs.All + """
            namespace MyApp
            {
                interface IMyService { }

                class MyService : IMyService
                {
                    public MyService(ITenantContextAccessor accessor) { }
                }

                class Startup
                {
                    void Register(IServiceCollection services)
                    {
                        services.AddTransient<IMyService, MyService>();
                    }
                }
            }
            """;

        await new CSharpAnalyzerTest<SingletonTenantAccessorAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task MKT003_AddSingleton_WithoutAccessorInCtor_NoDiagnostic()
    {
        var source = MultitenancyStubs.All + """
            namespace MyApp
            {
                interface IMyService { }

                class MyService : IMyService
                {
                    public MyService(string connectionString) { }
                }

                class Startup
                {
                    void Register(IServiceCollection services)
                    {
                        services.AddSingleton<IMyService, MyService>();
                    }
                }
            }
            """;

        await new CSharpAnalyzerTest<SingletonTenantAccessorAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    // Architect issue #3: typeof(T) form is a known v1 limitation — must not trigger
    [Fact]
    public async Task MKT003_TypeofForm_NoDiagnostic()
    {
        var source = MultitenancyStubs.All + """
            namespace MyApp
            {
                interface IMyService { }

                class MyService : IMyService
                {
                    public MyService(ITenantContextAccessor accessor) { }
                }

                class Startup
                {
                    void Register(IServiceCollection services)
                    {
                        services.AddSingleton(typeof(IMyService), typeof(MyService));
                    }
                }
            }
            """;

        await new CSharpAnalyzerTest<SingletonTenantAccessorAnalyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }
}
