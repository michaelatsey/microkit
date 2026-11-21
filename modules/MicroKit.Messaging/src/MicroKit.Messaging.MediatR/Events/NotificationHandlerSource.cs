using System.Reflection;

namespace MicroKit.Messaging.MediatR.Events;

/// <summary>
/// Supplies each <see cref="INotificationHandler{TNotification}"/> of a domain-event notification
/// as a local consumer of its own, guarded by its own inbox row (#188 D8).
/// </summary>
/// <remarks>
/// <para>
/// Read from the service collection once the container is built, so it sees every handler the host
/// registered, whatever the order of <c>AddMicroKitMediatR()</c> and
/// <c>UseMediatRDomainEvents()</c>. A domain-event notification is a type assignable to
/// <see cref="IDomainEventNotification{TEvent}"/>; any other notification is not a local message and
/// is left to MediatR.
/// </para>
/// <para>
/// <b>Never a silent skip (C3).</b> A local consumer is matched by the notification's exact runtime
/// type and named by its handler's type, so a descriptor that does not name both cannot be one: an
/// open generic <c>INotificationHandler&lt;&gt;</c>, a handler of an abstract or interface
/// domain-event notification, a factory or instance registration, and a singleton. Each is reported
/// as a start problem naming it, rather than leaving that handler to never run.
/// </para>
/// <para>
/// <b>Each consumer builds its own handler and nothing else</b>, from the delivery's scope: a
/// sibling whose dependency cannot be resolved never fails it. A transient or scoped handler is
/// activated by a factory prepared at the scan — each delivery has a scope of its own, so the two
/// lifetimes coincide — and the delivery disposes it. A singleton is rejected at start: it would be
/// shared by every delivery, and resolving it builds its siblings too.
/// </para>
/// <para>
/// <b>The consumer's inbox key is version-free</b> (<see cref="MessageConsumerKey"/>): it survives an
/// assembly-version bump, so a redelivery across a deployment still finds its row.
/// </para>
/// </remarks>
internal sealed class NotificationHandlerSource(IServiceCollection services) : ILocalConsumerSource
{
    private readonly Lazy<(List<LocalConsumer> Consumers, List<string> Problems)> _scan =
        new(() => Scan(services));

    /// <inheritdoc />
    public IEnumerable<LocalConsumer> GetConsumers() => _scan.Value.Consumers;

    /// <summary>The registrations that cannot be local consumers, each naming its fix.</summary>
    /// <returns>Every problem found; none means every handler is a consumer.</returns>
    public IEnumerable<string> Problems() => _scan.Value.Problems;

    private static (List<LocalConsumer>, List<string>) Scan(IServiceCollection services)
    {
        List<LocalConsumer> consumers = [];
        List<string> problems = [];

        foreach (var descriptor in services)
        {
            if (descriptor.IsKeyedService || !descriptor.ServiceType.IsGenericType)
            {
                continue;
            }

            if (descriptor.ServiceType == typeof(INotificationHandler<>))
            {
                problems.Add(
                    $"The open generic notification handler '{Name(descriptor)}' cannot be a local " +
                    "consumer: a domain-event notification is delivered to each closed handler of its " +
                    "type, with its own inbox row. Register it closed over each notification it handles.");
                continue;
            }

            if (descriptor.ServiceType.GetGenericTypeDefinition() != typeof(INotificationHandler<>))
            {
                continue;
            }

            var notification = descriptor.ServiceType.GenericTypeArguments[0];
            if (!IsDomainEventNotification(notification))
            {
                continue;
            }

            if (notification.IsAbstract || notification.IsInterface)
            {
                problems.Add(
                    $"Notification handler '{Name(descriptor)}' handles '{notification.FullName}', which " +
                    "is abstract: a domain-event notification is delivered to the handlers of its exact " +
                    "type, so this one would never run. Handle each concrete notification type.");
                continue;
            }

            if (descriptor.ImplementationType is not { } handlerType)
            {
                problems.Add(
                    $"The notification handler of '{notification.FullName}' is registered by " +
                    $"{(descriptor.ImplementationInstance is null ? "a factory" : "an instance")}, so its " +
                    "type — the key of its inbox row — is unknown. Register it by its type.");
                continue;
            }

            if (descriptor.Lifetime is ServiceLifetime.Singleton)
            {
                // A consumer is activated per delivery, in that delivery's scope, on its own: a
                // singleton would be shared by every delivery, and resolving it means building its
                // siblings too, so one that cannot be built would fail this one (#188, review of PR
                // #208). An instance registration, a singleton, is rejected above for its key.
                problems.Add(
                    $"Notification handler '{handlerType.FullName}' of '{notification.FullName}' is " +
                    "registered as a singleton. A notification handler is a local consumer of its own, " +
                    "activated per delivery in that delivery's scope: register it transient or scoped.");
                continue;
            }

            var factory = ActivatorUtilities.CreateFactory(descriptor.ImplementationType, Type.EmptyTypes);
            var invoker = (Func<Type, ObjectFactory, Func<IServiceProvider, object, MessageContext, CancellationToken, ValueTask>>)
                typeof(NotificationHandlerSource)
                    .GetMethod(nameof(InvokerFor), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(notification)
                    .Invoke(null, null)!;
            consumers.Add(new LocalConsumer(
                notification, MessageConsumerKey.Of(handlerType), handlerType, invoker(handlerType, factory)));
        }

        return (consumers, problems);
    }

    /// <summary>
    /// The invoker of one handler, closed over its notification type once, at the scan: a delivery
    /// calls a typed delegate, with no reflection.
    /// </summary>
    private static Func<Type, ObjectFactory, Func<IServiceProvider, object, MessageContext, CancellationToken, ValueTask>> InvokerFor<TNotification>()
        where TNotification : INotification
        => static (_, factory) => (services, message, _, ct) => HandleOwnedAsync(
            (INotificationHandler<TNotification>)factory(services, null), (TNotification)message, ct);

    /// <summary>Runs a handler this delivery activated, then disposes it: the scope does not track it.</summary>
    private static async ValueTask HandleOwnedAsync<TNotification>(
        INotificationHandler<TNotification> handler, TNotification notification, CancellationToken ct)
        where TNotification : INotification
    {
        try
        {
            await handler.Handle(notification, ct).ConfigureAwait(false);
        }
        finally
        {
            switch (handler)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="notification"/> is a domain event's: assignable to
    /// <see cref="IDomainEventNotification{TEvent}"/> by covariance, or — covariance does not reach a
    /// struct domain event — derived from <see cref="DomainEventNotification{TEvent}"/>.
    /// </summary>
    private static bool IsDomainEventNotification(Type notification)
    {
        if (typeof(IDomainEventNotification<IDomainEvent>).IsAssignableFrom(notification))
        {
            return true;
        }

        for (var type = notification; type is not null; type = type.BaseType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(DomainEventNotification<>))
            {
                return true;
            }
        }

        return false;
    }

    private static string Name(ServiceDescriptor descriptor)
        => descriptor.ImplementationType?.FullName ?? descriptor.ServiceType.FullName ?? descriptor.ServiceType.Name;
}
