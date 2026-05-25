namespace MicroKit.Logging.ArchitectureTests;

public sealed class NamingConventionTests
{
    private static readonly System.Collections.Generic.IReadOnlyList<Assembly> AllAssemblies =
    [
        typeof(LogPropertyNames).Assembly,        // Abstractions
        typeof(MicroKitLoggingOptions).Assembly,  // Core
        typeof(AspNetCoreLoggingOptions).Assembly, // AspNetCore
        typeof(MicroKitLogProcessor).Assembly,    // OpenTelemetry
        typeof(ActivitySources).Assembly,         // Diagnostics
    ];

    private static string FailingNames(TestResult result) =>
        result.FailingTypeNames is { } names ? string.Join(", ", names) : string.Empty;

    [Fact]
    public void TypesEndingWithLogEnricher_ImplementILogEnricher()
    {
        var result = Types
            .InAssemblies(AllAssemblies)
            .That().ArePublic()
            .And().AreNotInterfaces()
            .And().HaveNameEndingWith("LogEnricher")
            .Should().ImplementInterface(typeof(ILogEnricher))
            .GetResult();

        result.IsSuccessful.Should().BeTrue(because: FailingNames(result));
    }

    [Fact]
    public void TypesEndingWithOptions_AreSealed()
    {
        var result = Types
            .InAssemblies(AllAssemblies)
            .That().ArePublic()
            .And().HaveNameEndingWith("Options")
            .Should().BeSealed()
            .GetResult();

        result.IsSuccessful.Should().BeTrue(because: FailingNames(result));
    }

    [Fact]
    public void TypesEndingWithExtensions_AreStatic()
    {
        var result = Types
            .InAssemblies(AllAssemblies)
            .That().ArePublic()
            .And().HaveNameEndingWith("Extensions")
            .Should().BeStatic()
            .GetResult();

        result.IsSuccessful.Should().BeTrue(because: FailingNames(result));
    }

    [Fact]
    public void Enrichers_AreSealed()
    {
        var result = Types
            .InAssemblies(AllAssemblies)
            .That().ArePublic()
            .And().ImplementInterface(typeof(ILogEnricher))
            .Should().BeSealed()
            .GetResult();

        result.IsSuccessful.Should().BeTrue(because: FailingNames(result));
    }
}
