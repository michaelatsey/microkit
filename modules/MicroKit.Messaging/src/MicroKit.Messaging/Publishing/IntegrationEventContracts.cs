namespace MicroKit.Messaging.Publishing;

/// <summary>
/// One module's contribution to the application's published contract surface.
/// </summary>
/// <remarks>
/// Registered once per module and composed into a single <see cref="IntegrationEventRegistry"/>.
/// The <see cref="Source"/> lives here, on the contribution, rather than in a shared options
/// singleton — which is the defect this shape exists to prevent: a per-application value written
/// from a per-module call site is silently overwritten by whichever module registers last, and the
/// symptom is a wrong source on the wire.
/// </remarks>
public sealed class IntegrationEventContracts
{
    internal IntegrationEventContracts(
        string source, IReadOnlyList<IntegrationEventRegistration> registrations)
    {
        Source = source;
        Registrations = registrations;
    }

    /// <summary>Gets the emitting module, e.g. <c>/saasbtp/safety</c>.</summary>
    public string Source { get; }

    /// <summary>Gets the contracts this module publishes.</summary>
    public IReadOnlyList<IntegrationEventRegistration> Registrations { get; }
}
