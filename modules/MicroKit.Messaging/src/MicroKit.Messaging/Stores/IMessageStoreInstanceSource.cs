using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Primitives;

namespace MicroKit.Messaging.Spi;

/// <summary>
/// The instances of a partitioned store (#191): a snapshot of the instances to drain, a change token
/// that signals a new snapshot, and the mapping of a delivery to its instance.
/// </summary>
/// <remarks>
/// <para>
/// Part of the <b>store SPI</b>, configured with <see cref="MessageStoreBuilder.PartitionedBy{TSource, TInitializer}"/>
/// and registered as a keyed singleton. The shape is .NET's snapshot plus change token, as ASP.NET
/// Core's <c>EndpointDataSource</c> (<c>Endpoints</c> and <c>GetChangeToken()</c>) and options
/// monitoring use it: the engine re-reads <see cref="GetInstances"/> each time the token fires and
/// reconciles what it drains with that snapshot. A missed or coalesced signal therefore loses nothing:
/// the next snapshot carries every change.
/// </para>
/// <para>
/// <b>Declare an instance once its database is provisioned.</b> The engine creates no table and runs
/// no migration: hosts own their migrations.
/// </para>
/// <para>
/// A snapshot that throws, or holds one id twice, is logged and ignored: the engine keeps draining
/// what it drained, and reads again on the next signal. The token must raise its callbacks
/// (<see cref="IChangeToken.ActiveChangeCallbacks"/>): a <c>CancellationChangeToken</c> over a source
/// replaced on each change does.
/// </para>
/// <para>
/// Every member is called concurrently: <see cref="TryGetInstance"/> on every delivery guarded by the
/// store, so it must answer from memory, without I/O.
/// </para>
/// </remarks>
public interface IMessageStoreInstanceSource
{
    /// <summary>Gets the instances to drain now.</summary>
    /// <returns>The current snapshot; each id at most once.</returns>
    IReadOnlyCollection<MessageStoreInstance> GetInstances();

    /// <summary>Gets a token that fires when <see cref="GetInstances"/> has a new snapshot.</summary>
    /// <returns>The change token.</returns>
    IChangeToken GetChangeToken();

    /// <summary>
    /// Maps a delivery to the instance that holds its inbox rows — from the message's tenant, say.
    /// </summary>
    /// <param name="context">The message being delivered.</param>
    /// <param name="instance">The instance, when the delivery maps to one.</param>
    /// <returns>
    /// <see langword="true"/> when it maps to an instance. A delivery that maps to none, or to one the
    /// engine does not drain, is a counted failure the transport redelivers, never a pause of its
    /// endpoint.
    /// </returns>
    bool TryGetInstance(MessageContext context, [NotNullWhen(true)] out MessageStoreInstance? instance);
}
