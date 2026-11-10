using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MicroKit.Persistence.Analyzers.Tests;

/// <summary>
/// #197 phase 7: every MKP diagnostic's help link opens its own section of the module README — the link a
/// developer follows from the IDE leads somewhere that exists.
/// </summary>
public sealed partial class HelpLinkTests
{
    private const string ReadmeUrl = "https://github.com/michaelatsey/microkit/blob/main/modules/MicroKit.Persistence/README.md#";

    // Every analyzer of the assembly, found by reflection so a new one cannot be left out; the test assembly
    // is never trimmed.
#pragma warning disable IL2026, IL2067
    private static readonly IReadOnlyList<DiagnosticDescriptor> Descriptors = typeof(CollectionIncludeAnalyzer).Assembly
        .GetTypes()
        .Where(type => typeof(DiagnosticAnalyzer).IsAssignableFrom(type) && !type.IsAbstract && type.GetCustomAttribute<DiagnosticAnalyzerAttribute>() is not null)
        .SelectMany(type => ((DiagnosticAnalyzer)Activator.CreateInstance(type)!).SupportedDiagnostics)
        .ToList();
#pragma warning restore IL2026, IL2067

    [Fact]
    public void TheAnalyzers_DeclareExactlyMKP001ToMKP009()
    {
        var ids = Descriptors.Select(descriptor => descriptor.Id).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(Enumerable.Range(1, 9).Select(n => $"MKP{n:000}"), ids);
    }

    [Fact]
    public void EveryHelpLink_TargetsTheReadmeAnchorOfItsId()
    {
        foreach (var descriptor in Descriptors)
            Assert.Equal(ReadmeUrl + descriptor.Id.ToLowerInvariant(), descriptor.HelpLinkUri);
    }

    [Fact]
    public void EveryHelpLinkAnchor_IsTheAnchorOfExactlyOneReadmeHeading()
    {
        var anchors = HeadingAnchors(File.ReadAllText(Path.Combine(FindModuleRoot(), "README.md")));

        foreach (var descriptor in Descriptors)
        {
            var anchor = descriptor.HelpLinkUri[(descriptor.HelpLinkUri.IndexOf('#', StringComparison.Ordinal) + 1)..];
            Assert.True(anchors.Count(a => a == anchor) == 1, $"README.md has no single heading with the anchor #{anchor} ({descriptor.Id})");
        }
    }

    [Theory]
    [InlineData("### MKP001", "mkp001")]
    [InlineData("## Persistence failures as `Result`", "persistence-failures-as-result")]
    [InlineData("### MKP006 — two collections", "mkp006--two-collections")]
    public void HeadingAnchors_FollowGitHubsSlugRule(string heading, string anchor) =>
        Assert.Equal([anchor], HeadingAnchors(heading));

    [Fact]
    public void HeadingAnchors_NumberARepeatedSlug_AndSkipFencedCode() =>
        Assert.Equal(["usage", "usage-1"], HeadingAnchors("## Usage\n```bash\n# not a heading\n```\n## Usage\n"));

    // GitHub's rule: lower case; letters, digits, spaces, '-' and '_' kept, everything else dropped; spaces
    // become '-'; a repeated slug gets -1, -2, … Headings inside fenced code are not headings.
    private static List<string> HeadingAnchors(string markdown)
    {
        var anchors = new List<string>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var fenced = false;
        foreach (var line in markdown.Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                fenced = !fenced;
                continue;
            }

            var heading = Heading().Match(line);
            if (fenced || !heading.Success)
                continue;

            var slug = new StringBuilder();
            foreach (var c in heading.Groups["text"].Value.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c) || c is '-' or '_')
                    slug.Append(c);
                else if (c == ' ')
                    slug.Append('-');
            }

            var anchor = slug.ToString();
            if (seen.TryGetValue(anchor, out var count))
            {
                seen[anchor] = count + 1;
                anchor += "-" + (count + 1);
            }
            else
            {
                seen[anchor] = 0;
            }

            anchors.Add(anchor);
        }

        return anchors;
    }

    private static string FindModuleRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MicroKit.Persistence.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException("MicroKit.Persistence.slnx not found above " + AppContext.BaseDirectory);
    }

    [GeneratedRegex(@"^#{1,6}[ \t]+(?<text>.+?)[ \t]*#*\r?$")]
    private static partial Regex Heading();
}
