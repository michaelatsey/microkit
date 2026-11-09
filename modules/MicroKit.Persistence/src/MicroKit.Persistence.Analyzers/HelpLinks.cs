namespace MicroKit.Persistence.Analyzers;

/// <summary>
/// The help link of every MKP diagnostic: its own section of the module README, one <c>### MKPnnn</c> heading
/// per ID (<c>HelpLinkTests</c> checks that the anchor exists).
/// </summary>
internal static class HelpLinks
{
    private const string Readme = "https://github.com/michaelatsey/microkit/blob/main/modules/MicroKit.Persistence/README.md#";

    /// <summary>Returns the README anchor of <paramref name="diagnosticId"/>, e.g. <c>…/README.md#mkp001</c>.</summary>
    public static string For(string diagnosticId) => Readme + diagnosticId.ToLowerInvariant();
}
