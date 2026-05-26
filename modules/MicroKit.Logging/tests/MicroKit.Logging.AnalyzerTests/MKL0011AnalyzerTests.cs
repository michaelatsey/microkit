namespace MicroKit.Logging.AnalyzerTests;

public sealed class MKL0011AnalyzerTests
{
    // MEL stubs: the analyzer matches types by fully-qualified name.
    // The using directive below the namespace declaration brings LoggerExtensions extension methods into scope.
    private const string MelStubs = """
        using Microsoft.Extensions.Logging;

        namespace Microsoft.Extensions.Logging
        {
            public interface ILogger { }
            public static class LoggerExtensions
            {
                public static void LogDebug(this ILogger logger, string message, params object[] args) { }
                public static void LogTrace(this ILogger logger, string message, params object[] args) { }
                public static void LogInformation(this ILogger logger, string message, params object[] args) { }
                public static void LogWarning(this ILogger logger, string message, params object[] args) { }
                public static void LogError(this ILogger logger, string message, params object[] args) { }
                public static void LogCritical(this ILogger logger, string message, params object[] args) { }
            }
        }

        """;

    [Fact]
    public async Task MKL0011_InterpolatedString_RaisesWarning()
    {
        var source = MelStubs + """
            class Test
            {
                void M(ILogger logger, string name)
                {
                    logger.LogInformation({|MKL0011:$"Hello {name}"|});
                }
            }
            """;

        await new CSharpAnalyzerTest<MKL0011Analyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task MKL0011_StructuredTemplate_NoDiagnostic()
    {
        var source = MelStubs + """
            class Test
            {
                void M(ILogger logger, string name)
                {
                    logger.LogInformation("Hello {Name}", name);
                }
            }
            """;

        await new CSharpAnalyzerTest<MKL0011Analyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task MKL0011_StringLiteralOnly_NoDiagnostic()
    {
        var source = MelStubs + """
            class Test
            {
                void M(ILogger logger)
                {
                    logger.LogWarning("plain literal message");
                }
            }
            """;

        await new CSharpAnalyzerTest<MKL0011Analyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task MKL0011_NestedInterpolation_RaisesWarning()
    {
        var source = MelStubs + """
            class Test
            {
                void M(ILogger logger, bool flag, string a, string b)
                {
                    logger.LogError({|MKL0011:$"Value is {(flag ? a : b)}"|});
                }
            }
            """;

        await new CSharpAnalyzerTest<MKL0011Analyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task MKL0011_Fix_ConvertsToConcatenation()
    {
        var before = MelStubs + """
            class Test
            {
                void M(ILogger logger, string name)
                {
                    logger.LogInformation({|MKL0011:$"Hello {name}"|});
                }
            }
            """;

        var after = MelStubs + """
            class Test
            {
                void M(ILogger logger, string name)
                {
                    logger.LogInformation("Hello {Name}", name);
                }
            }
            """;

        await new CSharpCodeFixTest<MKL0011Analyzer, MKL0011CodeFixProvider, CompatXUnitVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync();
    }
}
