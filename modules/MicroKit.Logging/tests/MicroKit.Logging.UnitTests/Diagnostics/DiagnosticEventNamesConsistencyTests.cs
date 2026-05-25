using MicroKit.Logging.Diagnostics;
using MicroKit.Logging.Internal;

namespace MicroKit.Logging.UnitTests.Diagnostics;

public sealed class DiagnosticEventNamesConsistencyTests
{
    [Theory]
    [InlineData(DiagnosticEventNames.EnrichmentExecuted,   MicroKitDiagnosticSource.EnrichmentExecuted)]
    [InlineData(DiagnosticEventNames.EnrichmentFaulted,    MicroKitDiagnosticSource.EnrichmentFaulted)]
    [InlineData(DiagnosticEventNames.ScopeCreated,         MicroKitDiagnosticSource.ScopeCreated)]
    [InlineData(DiagnosticEventNames.ScopeDisposed,        MicroKitDiagnosticSource.ScopeDisposed)]
    [InlineData(DiagnosticEventNames.CorrelationResolved,  MicroKitDiagnosticSource.CorrelationResolved)]
    [InlineData(DiagnosticEventNames.CorrelationGenerated, MicroKitDiagnosticSource.CorrelationGenerated)]
    public void PublicEventName_MatchesCoreInternal(string publicName, string internalName)
        => publicName.Should().Be(internalName);
}
