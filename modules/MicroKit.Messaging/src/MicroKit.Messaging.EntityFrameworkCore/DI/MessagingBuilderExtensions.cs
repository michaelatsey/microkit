namespace MicroKit.Messaging.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>Extension methods on <see cref="MessagingBuilder"/> for the EF Core store.</summary>
public static class MessagingBuilderExtensions
{
    /// <summary>
    /// Makes <typeparamref name="TContext"/> a message store: the outbox and the inbox of that
    /// <see cref="DbContext"/>, drained by the engine with their own loops and back-off.
    /// </summary>
    /// <typeparam name="TContext">
    /// The application's <see cref="DbContext"/>. Its model must call
    /// <see cref="ModelBuilderExtensions.ApplyMessagingConfiguration"/>, and the host owns its
    /// migrations: none is shipped.
    /// </typeparam>
    /// <param name="builder">The builder handed to <c>AddMicroKitMessaging(bus =&gt; ...)</c>.</param>
    /// <param name="configure">Binds assemblies to the store; needed only with several stores.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <b>Outbox by enlistment.</b> Once a store is configured, <see cref="IEventPublisher.PublishAsync{T}"/>
    /// adds the message to the scope's <typeparamref name="TContext"/> and saves nothing. The
    /// caller's <c>SaveChanges</c> — whatever owns it — commits the message with the business data;
    /// there is no open-transaction precondition. A message still unsaved when its scope ends is
    /// reported at <c>Error</c> (event 4000); one detached from the change tracker, at <c>Debug</c>
    /// (event 4001) — discarded with its work, or saved and then cleared, in which case it is published at
    /// the next poll if its save committed.
    /// </para>
    /// <para>
    /// <b>Repeatable, one store per <typeparamref name="TContext"/>.</b> A second call for the same
    /// type adds to that store. A call for another <see cref="DbContext"/> adds a second store; with
    /// several, bind each handler's assembly with
    /// <see cref="EntityFrameworkOutboxBuilder.ForAssemblyContaining{TMarker}"/>, or the host fails
    /// to start. A message type is bound the same way: a publication outside any delivery whose
    /// type no binding resolves, and that names no <see cref="PublishOptions.Store"/>, fails at
    /// publication.
    /// </para>
    /// <para>
    /// <b>Checked at host start:</b> <typeparamref name="TContext"/> is registered, and its model
    /// carries the messaging entities. The model check builds one instance of the context, in a
    /// scope of its own. For a partitioned store
    /// (<see cref="EntityFrameworkOutboxBuilder.PartitionedBy{TSource, TInitializer}"/>, #191), whose
    /// context may only be buildable for an instance, the model check runs instead when its first
    /// instance starts, in that instance's prepared scope.
    /// </para>
    /// <para>
    /// Built on the public store SPI only (<see cref="MessageStoreRegistrationExtensions.AddMessageStore"/>);
    /// a store over another persistence technology registers itself the same way.
    /// </para>
    /// </remarks>
    public static MessagingBuilder UseEntityFrameworkOutbox<TContext>(
        this MessagingBuilder builder, Action<EntityFrameworkOutboxBuilder>? configure = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        var key = EntityFrameworkOutbox.KeyOf<TContext>();

        return builder.AddMessageStore(key, store =>
        {
            var services = store.Services;

            // A second call for the same TContext reaches the same store: its registrations are
            // already there, and its start check too.
            var first = !services.Any(d => d.ServiceType == typeof(EfOutboxStore<TContext>));
            if (first)
            {
                store.AddStartupCheck(CheckStore<TContext>);
                store.AddInstanceCheck(CheckInstanceModel<TContext>);
            }

            // One store instance per scope over the scope's TContext; every contract resolves to it.
            services.TryAddScoped<EfInboxAttempt<TContext>>();
            services.TryAddScoped<EfOutboxStore<TContext>>();
            services.TryAddKeyedScoped<IOutboxWriter>(key, static (sp, _) => sp.GetRequiredService<EfOutboxStore<TContext>>());
            services.TryAddKeyedScoped<IOutboxProcessorStore>(key, static (sp, _) => sp.GetRequiredService<EfOutboxStore<TContext>>());
            services.TryAddKeyedScoped<IOutboxAdminStore>(key, static (sp, _) => sp.GetRequiredService<EfOutboxStore<TContext>>());
            services.TryAddKeyedScoped<IOutboxRetentionStore>(key, static (sp, _) => sp.GetRequiredService<EfOutboxStore<TContext>>());

            services.TryAddScoped<EfInboxStore<TContext>>();
            services.TryAddKeyedScoped<IInboxGuardStore>(key, static (sp, _) => sp.GetRequiredService<EfInboxStore<TContext>>());
            services.TryAddKeyedScoped<IInboxProcessorStore>(key, static (sp, _) => sp.GetRequiredService<EfInboxStore<TContext>>());
            services.TryAddKeyedScoped<IInboxAdminStore>(key, static (sp, _) => sp.GetRequiredService<EfInboxStore<TContext>>());
            services.TryAddKeyedScoped<IInboxRetentionStore>(key, static (sp, _) => sp.GetRequiredService<EfInboxStore<TContext>>());

            if (configure is not null)
            {
                configure(new EntityFrameworkOutboxBuilder(store));
            }
        });
    }

    private static IEnumerable<string> CheckStore<TContext>(IServiceProvider services)
        where TContext : DbContext
    {
        var key = EntityFrameworkOutbox.KeyOf<TContext>();
        var name = typeof(TContext).FullName;

        if (!services.GetRequiredService<IServiceProviderIsService>().IsService(typeof(TContext)))
        {
            return [
                $"Store '{key}' is the outbox of '{name}', which is not registered. Register the " +
                $"context — services.AddDbContext<{typeof(TContext).Name}>(...) — before the host starts."];
        }

        // A partitioned store's context may only be buildable in an instance's scope: its model is
        // checked there, when its first instance starts (CheckInstanceModel).
        if (IsPartitioned(services, key))
        {
            return [];
        }

        IModel model;
        try
        {
            using var scope = services.CreateScope();
            model = scope.ServiceProvider.GetRequiredService<TContext>().Model;
        }
#pragma warning disable CA1031 // a start check reports every problem; it never throws one of them
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return [
                $"Store '{key}': '{name}' could not be built to check that its model carries the " +
                $"messaging entities ({ex.GetType().Name}: {ex.Message}). The check builds it in a " +
                "scope of its own, with no tenant or request."];
        }

        return MissingEntities<TContext>(model);
    }

    /// <summary>
    /// A partitioned store's model check (#191), in the prepared scope of its first instance. A context
    /// that cannot be built there throws: that instance's failure, retried; problems are the store's.
    /// </summary>
    private static IEnumerable<string> CheckInstanceModel<TContext>(IServiceProvider services)
        where TContext : DbContext
        => MissingEntities<TContext>(services.GetRequiredService<TContext>().Model);

    private static IEnumerable<string> MissingEntities<TContext>(IModel model)
        where TContext : DbContext
    {
        var missing = new List<string>(2);
        if (model.FindEntityType(typeof(OutboxMessage)) is null)
        {
            missing.Add(nameof(OutboxMessage));
        }

        if (model.FindEntityType(typeof(InboxMessage)) is null)
        {
            missing.Add(nameof(InboxMessage));
        }

        return missing.Count == 0
            ? []
            : [
                $"Store '{EntityFrameworkOutbox.KeyOf<TContext>()}': the model of '{typeof(TContext).FullName}' " +
                $"has no {string.Join(" or ", missing)} entity. " +
                "Call modelBuilder.ApplyMessagingConfiguration() in its OnModelCreating, and add " +
                "the tables to the host's migrations."];
    }

    /// <summary>
    /// Whether <paramref name="key"/> is given an instance source (#191), read without requiring
    /// <see cref="IServiceProviderIsKeyedService"/>: a container that lacks it answers by resolving the
    /// source, and a single store is checked as before on any container.
    /// </summary>
    private static bool IsPartitioned(IServiceProvider services, MessageStoreKey key)
        => services.GetService<IServiceProviderIsKeyedService>() is { } keyed
            ? keyed.IsKeyedService(typeof(IMessageStoreInstanceSource), key)
            : services is IKeyedServiceProvider provider
              && provider.GetKeyedService(typeof(IMessageStoreInstanceSource), key) is not null;
}
