namespace MicroKit.Logging.AnalyzerTests;

public sealed class MKL0041AnalyzerTests
{
    private const string MelStubs = """
        using Microsoft.Extensions.Logging;

        namespace Microsoft.Extensions.Logging
        {
            public enum LogLevel { Trace, Debug, Information, Warning, Error, Critical, None }
            public interface ILogger
            {
                bool IsEnabled(LogLevel logLevel);
            }
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
    public async Task MKL0041_ExpensiveCall_WithoutGuard_RaisesWarning()
    {
        var source = MelStubs + """
            class Test
            {
                static string ComputeState() => "state";

                void M(ILogger logger)
                {
                    {|MKL0041:logger.LogDebug("State {State}", ComputeState())|};
                }
            }
            """;

        await new CSharpAnalyzerTest<MKL0041Analyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task MKL0041_WithIsEnabledGuard_NoDiagnostic()
    {
        var source = MelStubs + """
            class Test
            {
                static string ComputeState() => "state";

                void M(ILogger logger)
                {
                    if (logger.IsEnabled(LogLevel.Debug))
                    {
                        logger.LogDebug("State {State}", ComputeState());
                    }
                }
            }
            """;

        await new CSharpAnalyzerTest<MKL0041Analyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task MKL0041_SimpleValueArg_NoDiagnostic()
    {
        var source = MelStubs + """
            class Test
            {
                private readonly string _name = "test";

                void M(ILogger logger)
                {
                    logger.LogDebug("Name {Name}", _name);
                }
            }
            """;

        await new CSharpAnalyzerTest<MKL0041Analyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task MKL0041_LiteralArg_NoDiagnostic()
    {
        var source = MelStubs + """
            class Test
            {
                void M(ILogger logger)
                {
                    logger.LogDebug("Count {Count}", 42);
                }
            }
            """;

        await new CSharpAnalyzerTest<MKL0041Analyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task MKL0041_Fix_WrapsInIsEnabled()
    {
        var before = MelStubs + """
            class Test
            {
                static string ComputeState() => "state";

                void M(ILogger logger)
                {
                    {|MKL0041:logger.LogDebug("State {State}", ComputeState())|};
                }
            }
            """;

        var after = MelStubs + """
            class Test
            {
                static string ComputeState() => "state";

                void M(ILogger logger)
                {
                    if (logger.IsEnabled(LogLevel.Debug))
                    {
                        logger.LogDebug("State {State}", ComputeState());
                    }
                }
            }
            """;

        await new CSharpCodeFixTest<MKL0041Analyzer, MKL0041CodeFixProvider, CompatXUnitVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync();
    }
}
