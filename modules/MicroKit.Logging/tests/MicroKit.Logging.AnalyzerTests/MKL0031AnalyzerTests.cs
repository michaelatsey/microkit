namespace MicroKit.Logging.AnalyzerTests;

public sealed class MKL0031AnalyzerTests
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
    public async Task MKL0031_PasswordKeyword_RaisesError()
    {
        var source = MelStubs + """
            class Test
            {
                void M(ILogger logger, string pwd)
                {
                    logger.LogInformation({|MKL0031:"User {UserId} entered {password}"|}, "u1", pwd);
                }
            }
            """;

        await new CSharpAnalyzerTest<MKL0031Analyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task MKL0031_CanonicalName_NoDiagnostic()
    {
        var source = MelStubs + """
            class Test
            {
                void M(ILogger logger, string correlationId)
                {
                    logger.LogInformation("Processing request {CorrelationId}", correlationId);
                }
            }
            """;

        await new CSharpAnalyzerTest<MKL0031Analyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task MKL0031_ApiKeyKeyword_RaisesError()
    {
        var source = MelStubs + """
            class Test
            {
                void M(ILogger logger, string s)
                {
                    logger.LogWarning({|MKL0031:"Config value {ApiKey} is set"|}, s);
                }
            }
            """;

        await new CSharpAnalyzerTest<MKL0031Analyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task MKL0031_TokenKeyword_RaisesError()
    {
        var source = MelStubs + """
            class Test
            {
                void M(ILogger logger, string t)
                {
                    logger.LogError({|MKL0031:"Auth failed for {token}"|}, t);
                }
            }
            """;

        await new CSharpAnalyzerTest<MKL0031Analyzer, CompatXUnitVerifier>
        {
            TestCode = source,
        }.RunAsync();
    }

    [Fact]
    public async Task MKL0031_Fix_ReplacesPlaceholderWithRedacted()
    {
        var before = MelStubs + """
            class Test
            {
                void M(ILogger logger, string pwd)
                {
                    logger.LogInformation({|MKL0031:"User {UserId} entered {password}"|}, "u1", pwd);
                }
            }
            """;

        var after = MelStubs + """
            class Test
            {
                void M(ILogger logger, string pwd)
                {
                    logger.LogInformation("User {UserId} entered {[Redacted]}", "u1", pwd);
                }
            }
            """;

        await new CSharpCodeFixTest<MKL0031Analyzer, MKL0031CodeFixProvider, CompatXUnitVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync();
    }

    [Theory]
    [InlineData("password")]
    [InlineData("passwd")]
    [InlineData("secret")]
    [InlineData("token")]
    [InlineData("apikey")]
    [InlineData("cvv")]
    [InlineData("ssn")]
    [InlineData("pin")]
    public void IsSensitiveTerm_KnownTerms_ReturnsTrue(string term)
    {
        MKL0031Analyzer.IsSensitiveTerm(term).ShouldBeTrue();
    }

    [Theory]
    [InlineData("CorrelationId")]
    [InlineData("UserId")]
    [InlineData("TenantId")]
    [InlineData("OperationId")]
    [InlineData("RequestId")]
    public void IsSensitiveTerm_CanonicalNames_ReturnsFalse(string name)
    {
        MKL0031Analyzer.IsSensitiveTerm(name).ShouldBeFalse();
    }
}
