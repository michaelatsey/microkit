namespace MicroKit.Logging.AnalyzerTests;

public sealed class MKL0012AnalyzerTests
{
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
    public async Task MKL0012_BinaryAdd_RaisesWarning()
    {
        var source = MelStubs + """
            class Test
            {
                void M(ILogger logger, string value)
                {
                    logger.LogWarning({|MKL0012:"Prefix: " + value|});
                }
            }
            """;

        await new CSharpAnalyzerTest<MKL0012Analyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task MKL0012_StringLiteralOnly_NoDiagnostic()
    {
        var source = MelStubs + """
            class Test
            {
                void M(ILogger logger)
                {
                    logger.LogWarning("just a literal");
                }
            }
            """;

        await new CSharpAnalyzerTest<MKL0012Analyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task MKL0012_StructuredTemplate_NoDiagnostic()
    {
        var source = MelStubs + """
            class Test
            {
                void M(ILogger logger, string value)
                {
                    logger.LogWarning("Prefix: {Value}", value);
                }
            }
            """;

        await new CSharpAnalyzerTest<MKL0012Analyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task MKL0012_ChainedConcat_RaisesWarning()
    {
        var source = MelStubs + """
            class Test
            {
                void M(ILogger logger, string a, string b)
                {
                    logger.LogError({|MKL0012:"a=" + a + " b=" + b|});
                }
            }
            """;

        await new CSharpAnalyzerTest<MKL0012Analyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task MKL0012_Fix_ExtractsOperand()
    {
        var before = MelStubs + """
            class Test
            {
                void M(ILogger logger, string value)
                {
                    logger.LogWarning({|MKL0012:"Prefix: " + value|});
                }
            }
            """;

        var after = MelStubs + """
            class Test
            {
                void M(ILogger logger, string value)
                {
                    logger.LogWarning("Prefix: {Value}", value);
                }
            }
            """;

        await new CSharpCodeFixTest<MKL0012Analyzer, MKL0012CodeFixProvider, CompatXUnitVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync();
    }
}
