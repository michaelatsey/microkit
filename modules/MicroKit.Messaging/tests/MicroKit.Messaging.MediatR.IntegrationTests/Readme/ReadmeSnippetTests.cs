using System.Text.RegularExpressions;

namespace MicroKit.Messaging.MediatR.IntegrationTests.Readme;

/// <summary>
/// The README's durable examples equal the regions compiled in this project (#162, #187). The
/// same check as <c>MicroKit.Messaging.UnitTests.Readme.ReadmeSnippetTests</c>, which also proves
/// that every marked README block has a region in one of the two files.
/// </summary>
public sealed partial class ReadmeSnippetTests
{
    [Fact]
    public void EveryRegionOfThisProject_EqualsItsReadmeBlock()
    {
        var root = ModuleRoot();
        var readme = File.ReadAllText(Path.Combine(root, "README.md")).ReplaceLineEndings("\n");
        var source = File.ReadAllText(
            Path.Combine(root, "tests/MicroKit.Messaging.MediatR.IntegrationTests/Readme/ReadmeSnippets.cs"))
            .ReplaceLineEndings("\n");

        var blocks = ReadmeBlock().Matches(readme)
            .ToDictionary(m => m.Groups["name"].Value, m => m.Groups["code"].Value, StringComparer.Ordinal);
        var regions = Region().Matches(source)
            .ToDictionary(m => m.Groups["name"].Value, m => m.Groups["code"].Value, StringComparer.Ordinal);

        regions.ShouldNotBeEmpty();
        foreach (var (name, code) in regions)
        {
            blocks.ShouldContainKey(name, $"region '{name}' has no README block");
            Normalize(blocks[name]).ShouldBe(Normalize(code), $"README snippet '{name}' must equal its compiled region");
        }
    }

    private static string ModuleRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MicroKit.Messaging.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Module root not found.");
    }

    private static string Normalize(string code) => Whitespace().Replace(code, " ").Trim();

    [GeneratedRegex(@"<!-- snippet: (?<name>[\w-]+) -->\s*```csharp\n(?<code>.*?)```", RegexOptions.Singleline)]
    private static partial Regex ReadmeBlock();

    [GeneratedRegex(@"// begin-snippet: (?<name>[\w-]+)\n(?<code>.*?)\n[ \t]*// end-snippet", RegexOptions.Singleline)]
    private static partial Regex Region();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
