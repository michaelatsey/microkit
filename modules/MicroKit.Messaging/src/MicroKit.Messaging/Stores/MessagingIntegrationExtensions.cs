using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroKit.Messaging.Spi;

/// <summary>
/// The registration path of an integration package: local consumers and start checks (#188).
/// </summary>
/// <remarks>
/// Part of the SPI: <c>MicroKit.Messaging.MediatR</c> composes with the bus through these and the
/// rest of the public surface only. Application code does not call them.
/// </remarks>
public static class MessagingIntegrationExtensions
{
    /// <summary>
    /// Adds a source of local consumers: what a message sent through
    /// <see cref="ILocalMessageSender"/> is delivered to.
    /// </summary>
    /// <param name="builder">The messaging builder.</param>
    /// <param name="source">The source; a second source of the same type is ignored.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// The source is read once, when the bus first needs its consumers, after the container is
    /// built. With several stores, each consumer's handler type must sit in an assembly bound to
    /// one of them: an unbound one fails at host start, as an unbound handler does.
    /// </remarks>
    public static MessagingBuilder AddLocalConsumerSource(this MessagingBuilder builder, ILocalConsumerSource source)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Composition.AddLocalConsumerSource(source);

        // The in-memory queue's worker, for a host with no store: idle on an empty queue.
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, Local.LocalQueueWorker>());
        return builder;
    }

    /// <summary>Adds a check run at host start, with the bus's own composition checks.</summary>
    /// <param name="builder">The messaging builder.</param>
    /// <param name="check">Returns every problem it finds, each naming its fix; none means valid.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// Every problem of every check is reported together, in one
    /// <see cref="IntegrationEventConfigurationException"/> thrown before any hosted service starts.
    /// </remarks>
    public static MessagingBuilder AddStartupCheck(
        this MessagingBuilder builder, Func<IServiceProvider, IEnumerable<string>> check)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Composition.AddStartupCheck(check);
        return builder;
    }
}
