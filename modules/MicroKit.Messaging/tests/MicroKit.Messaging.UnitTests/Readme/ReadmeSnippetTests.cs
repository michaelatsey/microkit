namespace MicroKit.Messaging.UnitTests.Readme;

using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;

/// <summary>
/// The README's examples compile because they are code (#162, #187): every C# block marked
/// <c>&lt;!-- snippet: name --&gt;</c> equals a region of a compiled file, whitespace-normalized.
/// </summary>
public sealed partial class ReadmeSnippetTests
{
    private const string UnitTestsSnippets = "tests/MicroKit.Messaging.UnitTests/Readme/ReadmeSnippets.cs";
    private const string DurableSnippets = "tests/MicroKit.Messaging.MediatR.IntegrationTests/Readme/ReadmeSnippets.cs";

    [Fact]
    public void EveryMarkedReadmeBlock_HasARegion_AndEveryRegionIsInTheReadme()
    {
        var blocks = ReadmeBlocks();
        var regions = Regions(UnitTestsSnippets).Concat(Regions(DurableSnippets)).ToDictionary();

        blocks.ShouldNotBeEmpty("the README marks its compiled examples");
        blocks.Keys.Order(StringComparer.Ordinal).ShouldBe(
            regions.Keys.Order(StringComparer.Ordinal),
            "a marked block without a region is not compiled; a region without a block is stale");
    }

    [Fact]
    public void EveryMarkedReadmeBlock_EqualsItsRegion()
    {
        var blocks = ReadmeBlocks();
        var regions = Regions(UnitTestsSnippets).Concat(Regions(DurableSnippets)).ToDictionary();

        foreach (var (name, code) in blocks)
        {
            regions.ShouldContainKey(name);
            Normalize(code).ShouldBe(
                Normalize(regions[name]),
                $"README snippet '{name}' must equal its compiled region");
        }
    }

    /// <summary>Spec, verification 1: publish and consume in fewer than 10 lines of configuration.</summary>
    [Fact]
    public void TheQuickStart_HasFewerThanTenConfigurationLines()
    {
        var lines = Regions(UnitTestsSnippets)["quick-start"]
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("//", StringComparison.Ordinal))
            .ToList();

        lines.Count.ShouldBeLessThan(10, string.Join(Environment.NewLine, lines));
    }

    /// <summary>
    /// Spec, verification 1, run: the quick start, as the README writes it, publishes and consumes
    /// through the in-memory transport.
    /// </summary>
    [Fact]
    public async Task TheQuickStart_PublishesAndConsumesThroughTheInMemoryTransport()
    {
        var logs = new CapturingLoggerProvider();
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Logging.AddProvider(logs);
        ReadmeSnippets.QuickStart(builder.Services);

        using var host = builder.Build();
        await host.StartAsync();
        var orderId = Guid.NewGuid();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await ActivatorUtilities.CreateInstance<Checkout>(scope.ServiceProvider).PlaceAsync(orderId);
        }

        await Bus.Eventually.HoldsAsync(
            () => logs.Messages.Any(m => m.Contains(orderId.ToString(), StringComparison.Ordinal)),
            "the handler ran");
        host.Services.GetRequiredService<InMemoryBroker>().GetDeadLetters("shop.orders").ShouldBeEmpty();
        await host.StopAsync();
    }

    [Fact]
    public async Task TheEndToEndExample_Runs()
        => await ReadmeSnippets.EndToEndAsync([]);

    internal static string ModuleRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MicroKit.Messaging.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Module root not found.");
    }

    private static Dictionary<string, string> ReadmeBlocks()
    {
        var readme = File.ReadAllText(Path.Combine(ModuleRoot(), "README.md")).ReplaceLineEndings("\n");
        return ReadmeBlock().Matches(readme)
            .ToDictionary(m => m.Groups["name"].Value, m => m.Groups["code"].Value, StringComparer.Ordinal);
    }

    private static Dictionary<string, string> Regions(string relativePath)
    {
        var source = File.ReadAllText(Path.Combine(ModuleRoot(), relativePath)).ReplaceLineEndings("\n");
        return Region().Matches(source)
            .ToDictionary(m => m.Groups["name"].Value, m => m.Groups["code"].Value, StringComparer.Ordinal);
    }

    private static string Normalize(string code) => Whitespace().Replace(code, " ").Trim();

    [GeneratedRegex(@"<!-- snippet: (?<name>[\w-]+) -->\s*```csharp\n(?<code>.*?)```", RegexOptions.Singleline)]
    private static partial Regex ReadmeBlock();

    [GeneratedRegex(@"// begin-snippet: (?<name>[\w-]+)\n(?<code>.*?)\n[ \t]*// end-snippet", RegexOptions.Singleline)]
    private static partial Regex Region();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

/// <summary>Captures every formatted log message, for tests that observe a handler through its log.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<(string Category, LogLevel Level, EventId Id, string Message)> _entries = new();

    public IReadOnlyList<(string Category, LogLevel Level, EventId Id, string Message)> Entries => [.. _entries];

    public IReadOnlyList<string> Messages => [.. _entries.Select(e => e.Message)];

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class Logger(
        string category,
        System.Collections.Concurrent.ConcurrentQueue<(string, LogLevel, EventId, string)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => entries.Enqueue((category, logLevel, eventId, formatter(state, exception)));
    }
}
