using System.Text.RegularExpressions;

namespace MicroKit.Persistence.ArchitectureTests;

/// <summary>
/// #196 D16: every C# sample of the module README exists in samples/MicroKit.Persistence.Samples,
/// which builds in Release — so a README sample can neither stop compiling nor drift from the API.
/// </summary>
public sealed partial class ReadmeSamplesTests
{
    [Fact]
    public void EveryReadmeCSharpSample_IsCompiledByTheSamplesProject()
    {
        var moduleRoot = FindModuleRoot();
        var readme = File.ReadAllText(Path.Combine(moduleRoot, "README.md"));
        // Each file on its own: a sample must live in one file, never straddle two.
        var sampleFiles = Directory.EnumerateFiles(Path.Combine(moduleRoot, "samples", "MicroKit.Persistence.Samples"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => Normalise(File.ReadAllText(path)))
            .ToList();

        var blocks = CSharpBlock().Matches(readme).Select(m => m.Groups["code"].Value).ToList();

        blocks.ShouldNotBeEmpty("the README documents the API with C# samples");
        var missing = blocks
            .Where(block => !sampleFiles.Exists(file => file.Contains(Normalise(block), StringComparison.Ordinal)))
            .ToList();
        missing.ShouldBeEmpty("each README sample must appear verbatim (whitespace aside) in one file of the samples project");
    }

    private static string Normalise(string code) => Whitespace().Replace(code, " ").Trim();

    private static string FindModuleRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MicroKit.Persistence.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException("MicroKit.Persistence.slnx not found above " + AppContext.BaseDirectory);
    }

    // Every C# fence tag GitHub recognises.
    [GeneratedRegex(@"```(?:csharp|cs|c#)[ \t]*\r?\n(?<code>.*?)```", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex CSharpBlock();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
