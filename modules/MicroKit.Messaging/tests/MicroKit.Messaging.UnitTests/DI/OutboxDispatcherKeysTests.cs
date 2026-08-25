namespace MicroKit.Messaging.UnitTests.DI;

/// <summary>
/// Pins the literal value of <see cref="OutboxDispatcherKeys.Standard"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every other test in this repository references the symbol, which is exactly why this one is
/// needed.</b> The key links a decorator in one package to its inner in another, and the type's own
/// remarks say what changing it costs: the decorator finds nothing, and every
/// <see cref="MessageKind.Contract"/> row starts failing as a configuration fault in a deployment
/// where nothing about the composition changed. A suite that only ever names the symbol follows the
/// rename silently and stays green through precisely that break.
/// </para>
/// <para>
/// <b>The hazard is sharper than a source change, because the field is a <c>const</c>.</b> C#
/// inlines a <c>const</c> into the consumer's IL at compile time, and
/// <c>MicroKit.Messaging.MediatR</c> ships with an open lower-bound dependency on
/// <c>MicroKit.Messaging</c> — so a consumer may legitimately float Core forward while the glue
/// stays put. Change this string, and a glue assembly that was never recompiled keeps looking up
/// the old literal: the keyed lookup yields <see langword="null"/>, the host silently degrades to
/// notification-only, and contract rows stop. <c>const</c> is still the right shape — it is what
/// lets a third-party decorator write <c>[FromKeyedServices(OutboxDispatcherKeys.Standard)]</c>,
/// which <c>static readonly</c> would forbid — so the value is guarded instead of the shape being
/// changed.
/// </para>
/// <para>
/// Same rationale as the contract-name snapshot: a key that crosses a package boundary is public
/// API, and a snapshot is the only assertion that can fail on a rename.
/// </para>
/// </remarks>
public sealed class OutboxDispatcherKeysTests
{
    [Fact]
    public void Standard_MatchesTheSnapshot()
    {
        // Deliberately a literal, not nameof or a computed string. If this fails, the key changed:
        // treat it as a breaking change to MicroKit.Messaging.Abstractions, not as a stale test.
        // Every already-compiled decorator in the wild carries the OLD value inlined.
        OutboxDispatcherKeys.Standard.ShouldBe("microkit.messaging.outbox-dispatcher.standard");
    }
}
