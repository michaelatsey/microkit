using MicroKit.MediatR.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MicroKit.MediatR.IntegrationTests.Registration;

/// <summary>
/// The composition contract of domain-event dispatch (ADR-MEDIATR-014): ONE
/// <see cref="IDomainEventsDispatcher"/> — the core orchestrator — plus an ordered, possibly empty
/// <c>IEnumerable&lt;IDomainEventsSink&gt;</c>. The orchestrator owns drain, the
/// <see cref="IDomainEventHandler{TEvent}"/> pass, and the barrier between them and the sinks.
/// </summary>
/// <remarks>
/// <para>
/// This class was <c>DomainEventsDispatcherRegistrationTests</c>, which tested the
/// ADR-MEDIATR-013 precedence contract between two rival dispatchers. There is now one
/// implementation, so the prior-registration stub no longer simulates the Messaging glue — it
/// simulates a <b>consumer override</b>, which is still supported and still load-bearing. Two of
/// the four recorded mutants survive that reframing and are recorded below.
/// </para>
/// <para>
/// Mutants, measured against the full module suite:
/// <list type="table">
///   <item><term>M1</term><description><c>IDomainEventsDispatcher</c> → <c>Add</c>: fails
///         <c>…KeepsExistingRegistration</c> and <c>…RegistersDispatcherOnce</c>.</description></item>
///   <item><term>M4</term><description>concrete <c>DomainEventDispatcher</c> → <c>Add</c>, interface
///         left as <c>TryAdd</c>: fails <c>…RegistersDispatcherOnce</c> on the concrete descriptor
///         count. Still the load-bearing one — no behavioural test can reach that
///         descriptor.</description></item>
///   <item><term>NEW</term><description>delete the sink loop from the orchestrator: fails
///         <c>…SinkReceivesBatchAfterAllHandlersRan</c> here, and the two outbox staging tests in
///         MicroKit.Messaging.</description></item>
///   <item><term>NEW</term><description><c>break</c> after the first sink, reverse iteration of
///         <c>_sinks</c>, or handing each sink its own copy of the batch: all three fail
///         <c>…TwoSinksRegistered_InvokesEachInOrderWithTheSameBatch</c>, and none of them is
///         reachable by a single-sink test.</description></item>
/// </list>
/// M2 and M3 lose their glue framing and are no longer recorded as distinct.
/// </para>
/// Every assertion resolves through a real <see cref="ServiceProvider"/>; nothing here inspects
/// <see cref="ServiceDescriptor"/>s except where a descriptor count is the only observable.
/// </remarks>
public sealed class DomainEventDispatchCompositionTests
{
    // The core implementation is internal and the module ships no InternalsVisibleTo, so it is
    // located by name off the core assembly rather than referenced directly. Adding an
    // InternalsVisibleTo purely for a test would widen a shipped package's surface.
    private static readonly Type CoreDispatcherType =
        typeof(IDomainEventsDispatcher).Assembly
            .GetType("MicroKit.MediatR.Events.DomainEventDispatcher", throwOnError: true)!;

    // ── the orchestrator is the only dispatcher, and stays a default ────────────────────────

    [Fact]
    public void AddMicroKitMediatR_WhenCalledAlone_ResolvesCoreDispatcher()
    {
        var services = NewServices();

        services.AddMicroKitMediatR(ScanAssemblyWithoutHandlers);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IDomainEventsDispatcher>()
            .ShouldBeOfType(CoreDispatcherType);
    }

    [Fact]
    public void AddMicroKitMediatR_WhenDispatcherAlreadyRegistered_KeepsIt()
    {
        // PR #84's TryAdd, re-purposed. It no longer arbitrates between MicroKit packages — the
        // glue registers no dispatcher at all now — but it still protects a CONSUMER who registered
        // their own before this call. Reverting it to Add remains a defect.
        var services = NewServices();
        services.AddScoped<IDomainEventsDispatcher, ConsumerDispatcher>();

        services.AddMicroKitMediatR(ScanAssemblyWithoutHandlers);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IDomainEventsDispatcher>()
            .ShouldBeOfType<ConsumerDispatcher>();
    }

    [Fact]
    public void AddMicroKitMediatR_WhenCalledTwice_RegistersDispatcherOnce()
    {
        // Scanning a handler-free assembly isolates the dispatcher descriptors: a second scan of an
        // assembly that DOES declare handlers would also re-register the handler map and the
        // notification factory (a separate, unrelated defect — last-wins on those singletons).
        var services = NewServices();

        services.AddMicroKitMediatR(ScanAssemblyWithoutHandlers);
        services.AddMicroKitMediatR(ScanAssemblyWithoutHandlers);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // All three dispatcher descriptors are asserted: the rule has no exceptions, so neither does
        // its test. Each assertion fails independently if its own registration reverts to Add.
        scope.ServiceProvider.GetServices(CoreDispatcherType).Count().ShouldBe(1);
        scope.ServiceProvider.GetServices<IDomainEventsDispatcher>().Count().ShouldBe(1);
#pragma warning disable CS0618 // The [Obsolete] alias is deliberately under test — the rule has no exceptions.
        scope.ServiceProvider.GetServices<IDomainEventDispatcher>().Count().ShouldBe(1);
#pragma warning restore CS0618
    }

    [Fact]
    public void AddMicroKitMediatR_WhenCalledAlone_ConcreteDispatcherStillResolves()
    {
        var services = NewServices();

        services.AddMicroKitMediatR(ScanAssemblyWithoutHandlers);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService(CoreDispatcherType).ShouldNotBeNull();
    }

    [Fact]
    public void AddMicroKitMediatR_WhenDispatcherAlreadyRegistered_ConcreteDispatcherStillResolves()
    {
        // The concrete descriptor is registered unconditionally — it must not be skipped merely
        // because the interface slot was already taken. With a consumer override it is inert.
        var services = NewServices();
        services.AddScoped<IDomainEventsDispatcher, ConsumerDispatcher>();

        services.AddMicroKitMediatR(ScanAssemblyWithoutHandlers);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService(CoreDispatcherType).ShouldNotBeNull();
    }

    [Fact]
    public void AddMicroKitMediatR_RegistersNoSinks()
    {
        // MicroKit.MediatR contributes the orchestrator and ZERO sinks. Installing a higher-level
        // package is what adds one.
        var services = NewServices();

        services.AddMicroKitMediatR(ScanAssemblyWithoutHandlers);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetServices<IDomainEventsSink>().ShouldBeEmpty();
    }

    // ── the sink seam ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchEventsAsync_WhenNoSinkRegistered_DispatchesHandlersOnly()
    {
        // The supported handlers-only configuration: MicroKit.MediatR without any outbox.
        // HandlerOnlyEvent deliberately has NO notification, so nothing can be lost and the
        // ADR-MEDIATR-015 guard must stay silent.
        var log = new DomainEventLog();
        using var provider = BuildWithFixtures(log);
        using var scope = provider.CreateScope();
        Raise(scope, new HandlerOnlyEvent(Guid.NewGuid()));

        await Dispatcher(scope).DispatchEventsAsync();

        log.HandlerOnlyInvocations.ShouldBe(1);
    }

    [Fact]
    public async Task DispatchEventsAsync_WhenSinkRegistered_ReceivesBatchAfterAllHandlersRan()
    {
        // THE BARRIER. P2 must complete for EVERY event before ANY sink runs, so a sink can never
        // observe a half-dispatched batch. Deleting the barrier (fusing the two loops) makes
        // HandlerInvocationsWhenReceived read 1 instead of 2.
        var log = new DomainEventLog();
        var sink = new RecordingSink(log);
        using var provider = BuildWithFixtures(log, Contribute(sink));
        using var scope = provider.CreateScope();
        Raise(scope, new HandlerOnlyEvent(Guid.NewGuid()), new HandlerOnlyEvent(Guid.NewGuid()));

        await Dispatcher(scope).DispatchEventsAsync();

        sink.Batches.Count.ShouldBe(1, "the sink receives the whole drained batch exactly once");
        sink.Batches[0].Count.ShouldBe(2);
        sink.HandlerInvocationsWhenReceived.ShouldBe(
            2, "every handler must have run for every event before any sink was invoked");
        log.HandlerOnlyInvocations.ShouldBe(2);
    }

    [Fact]
    public async Task DispatchEventsAsync_WhenBatchIsEmpty_DoesNotInvokeAnySink()
    {
        var log = new DomainEventLog();
        var sink = new RecordingSink(log);
        using var provider = BuildWithFixtures(log, Contribute(sink));
        using var scope = provider.CreateScope();

        // Nothing raised — the drain returns empty.
        await Dispatcher(scope).DispatchEventsAsync();

        sink.Batches.ShouldBeEmpty("IDomainEventsSink is never called with an empty batch");
    }

    [Fact]
    public async Task DispatchEventsAsync_WhenSinkThrows_Propagates()
    {
        // Sinks are fail-fast: a throwing sink aborts the command, which is what lets
        // TransactionBehavior roll the transaction back.
        var log = new DomainEventLog();
        using var provider = BuildWithFixtures(log, Contribute(new ThrowingSink()));
        using var scope = provider.CreateScope();
        Raise(scope, new HandlerOnlyEvent(Guid.NewGuid()));

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            async () => await Dispatcher(scope).DispatchEventsAsync());

        ex.Message.ShouldBe(ThrowingSink.Message);
    }

    [Fact]
    public async Task DispatchEventsAsync_WhenTwoSinksRegistered_InvokesEachInOrderWithTheSameBatch()
    {
        // Three documented guarantees that no single-sink test can pin, because with one sink every
        // mutant below still produces exactly one correct call:
        //   • "every registered sink"      — `break` after the first survives
        //   • "in registration order"      — iterating _sinks in reverse survives
        //   • "the same instance is passed" — a per-sink defensive copy survives
        var log = new DomainEventLog();
        var calls = new SinkCallLog();
        using var provider = BuildWithFixtures(
            log, Contribute(new FirstSink(calls), new SecondSink(calls)));
        using var scope = provider.CreateScope();
        Raise(scope, new HandlerOnlyEvent(Guid.NewGuid()), new HandlerOnlyEvent(Guid.NewGuid()));

        await Dispatcher(scope).DispatchEventsAsync();

        calls.Calls.Count.ShouldBe(2, "every registered sink receives the batch — none is skipped");
        calls.Calls[0].Sink.ShouldBe(nameof(FirstSink), "sinks run in registration order");
        calls.Calls[1].Sink.ShouldBe(nameof(SecondSink), "sinks run in registration order");
        calls.Calls[1].Batch.ShouldBeSameAs(
            calls.Calls[0].Batch,
            "the same list instance reaches every sink — a per-sink copy would break the "
            + "documented rule that mutating it changes what later sinks see");
        calls.Calls[0].Batch.Count.ShouldBe(2);
    }

    // ── ADR-MEDIATR-015: a notification with no sink is a configuration error, not silence ──

    [Fact]
    public async Task DispatchEventsAsync_WhenMappedEventRaisedAndNoSink_ThrowsNamingTheRegistration()
    {
        // ItemCreatedEvent maps to ItemCreatedNotification, and nothing is registered to receive it.
        // Previously that notification was created by nobody and discarded in silence: the app
        // started, ran, and published nothing.
        var log = new DomainEventLog();
        using var provider = BuildWithFixtures(log);
        using var scope = provider.CreateScope();
        Raise(scope, new ItemCreatedEvent(Guid.NewGuid()));

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            async () => await Dispatcher(scope).DispatchEventsAsync());

        // It must name what is missing and what to call — not read like a DI resolution failure.
        ex.Message.ShouldContain(nameof(ItemCreatedEvent));
        ex.Message.ShouldContain(nameof(ItemCreatedNotification));
        ex.Message.ShouldContain(nameof(IDomainEventsSink));
        ex.Message.ShouldContain("AddMediatRDomainEvents()");
    }

    [Fact]
    public async Task DispatchEventsAsync_WhenUnmappedEventRaisedAndNoSink_DoesNotThrow()
    {
        // The guard is per drained event that actually maps. HandlerOnlyEvent loses nothing, so the
        // notifications declared elsewhere in this assembly must not implicate it.
        var log = new DomainEventLog();
        using var provider = BuildWithFixtures(log);
        using var scope = provider.CreateScope();
        Raise(scope, new HandlerOnlyEvent(Guid.NewGuid()));

        await Should.NotThrowAsync(async () => await Dispatcher(scope).DispatchEventsAsync());
    }

    [Fact]
    public async Task DispatchEventsAsync_WhenMappedEventRaisedAndSinkRegistered_DoesNotThrow()
    {
        var log = new DomainEventLog();
        var sink = new RecordingSink(log);
        using var provider = BuildWithFixtures(log, Contribute(sink));
        using var scope = provider.CreateScope();
        Raise(scope, new ItemCreatedEvent(Guid.NewGuid()));

        await Dispatcher(scope).DispatchEventsAsync();

        sink.Batches.Count.ShouldBe(1);
    }

    // ── fixtures ───────────────────────────────────────────────────────────────────────────

    // IDomainEventsProvider is the one dependency of the core dispatcher that AddMicroKitMediatR
    // does not register itself — it comes from the consumer's persistence layer.
    private static ServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IDomainEventsProvider>());
        return services;
    }

    // AddMicroKitMediatR requires at least one assembly — MediatR throws "No assemblies found to
    // scan" otherwise. The MicroKit.MediatR core assembly declares no handlers and no notifications,
    // so scanning it registers nothing that could interfere with the dispatcher descriptors under
    // test, and a second AddMicroKitMediatR call adds no handler registrations either.
    private static void ScanAssemblyWithoutHandlers(MediatRBuilder builder)
        => builder.FromAssembly(typeof(IDomainEventsDispatcher).Assembly);

    // A container scanning this assembly's fixtures, so both a mapped event (ItemCreatedEvent) and
    // an unmapped one (HandlerOnlyEvent) exist — which is what makes the per-event precision of the
    // ADR-MEDIATR-015 guard observable.
    private static ServiceProvider BuildWithFixtures(
        DomainEventLog log,
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddScoped<StubDomainEventsProvider>();
        services.AddScoped<IDomainEventsProvider>(sp => sp.GetRequiredService<StubDomainEventsProvider>());
        services.AddMicroKitMediatR(cfg => cfg.FromAssemblyContaining<DomainEventLog>());
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    // Registered as an INSTANCE so the test can hold a reference to the same object it asserts on.
    // TryAddEnumerable needs a descriptor whose implementation type it can recover, to deduplicate
    // on (ServiceType, ImplementationType): by type, by instance, or via the two-type-argument
    // factory overload all qualify. Only the one-type-argument
    // ServiceDescriptor.Scoped<IDomainEventsSink>(sp => ...) is rejected — a bare lambda carries no
    // implementation type, so it throws "indistinguishable from other services".
    // Variadic so a PAIR can be contributed. TryAddEnumerable deduplicates on
    // (ServiceType, ImplementationType), so two sinks of the same class collapse into one
    // registration — a two-sink test needs two distinct implementation types, not two instances.
    private static Action<IServiceCollection> Contribute(params IDomainEventsSink[] sinks)
        => services =>
        {
            foreach (var sink in sinks)
                services.TryAddEnumerable(ServiceDescriptor.Singleton(sink));
        };

    private static void Raise(IServiceScope scope, params IDomainEvent[] domainEvents)
    {
        var provider = scope.ServiceProvider.GetRequiredService<StubDomainEventsProvider>();
        foreach (var domainEvent in domainEvents) provider.Raise(domainEvent);
    }

    private static IDomainEventsDispatcher Dispatcher(IServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IDomainEventsDispatcher>();

    private sealed class ConsumerDispatcher : IDomainEventsDispatcher
    {
        public Task DispatchEventsAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubDomainEventsProvider : IDomainEventsProvider
    {
        private readonly List<IDomainEvent> _events = [];

        public IReadOnlyList<IDomainEvent> DomainEvents => _events;

        public void Raise(IDomainEvent domainEvent) => _events.Add(domainEvent);

        public IReadOnlyList<IDomainEvent> DrainDomainEvents()
        {
            var drained = _events.ToArray();
            _events.Clear();
            return drained;
        }
    }

    private sealed class RecordingSink(DomainEventLog log) : IDomainEventsSink
    {
        // List, not IReadOnlyList: CA1859 — a test double's own collection type is concrete.
        public List<IReadOnlyList<IDomainEvent>> Batches { get; } = [];

        /// <summary>Handler invocations recorded at the moment the first batch arrived.</summary>
        public int HandlerInvocationsWhenReceived { get; private set; }

        public ValueTask ReceiveAsync(IReadOnlyList<IDomainEvent> domainEvents, CancellationToken ct = default)
        {
            if (Batches.Count == 0) HandlerInvocationsWhenReceived = log.HandlerOnlyInvocations;
            Batches.Add(domainEvents);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Records which sink was called, in call order, with the batch it received.</summary>
    private sealed class SinkCallLog
    {
        public List<(string Sink, IReadOnlyList<IDomainEvent> Batch)> Calls { get; } = [];
    }

    // Two distinct types, deliberately: see the comment on Contribute.
    private sealed class FirstSink(SinkCallLog log) : IDomainEventsSink
    {
        public ValueTask ReceiveAsync(IReadOnlyList<IDomainEvent> domainEvents, CancellationToken ct = default)
        {
            log.Calls.Add((nameof(FirstSink), domainEvents));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SecondSink(SinkCallLog log) : IDomainEventsSink
    {
        public ValueTask ReceiveAsync(IReadOnlyList<IDomainEvent> domainEvents, CancellationToken ct = default)
        {
            log.Calls.Add((nameof(SecondSink), domainEvents));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingSink : IDomainEventsSink
    {
        internal const string Message = "sink refused the batch";

        public ValueTask ReceiveAsync(IReadOnlyList<IDomainEvent> domainEvents, CancellationToken ct = default)
            => throw new InvalidOperationException(Message);
    }
}
