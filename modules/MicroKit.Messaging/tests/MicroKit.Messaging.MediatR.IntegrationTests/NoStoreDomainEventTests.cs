using System.Text;
using System.Text.Json;
using MicroKit.Messaging.MediatR.DependencyInjection;
using MicroKit.MediatR.Behaviors.DependencyInjection;
using MicroKit.Persistence.Abstractions;
using MicroKit.Persistence.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MicroKit.Messaging.MediatR.IntegrationTests;

/// <summary>
/// A host with no store (#188, review of PR #208): there is no engine save after a handler, so the
/// domain events still pending when its scope ends are never dispatched by the bus — they would run
/// in-transaction handlers whose writes nothing saves — and are reported instead.
/// </summary>
public sealed class NoStoreDomainEventTests
{
    [Fact]
    public async Task WithoutAStore_EventsPendingWhenAHandlersScopeEnds_AreNotDispatched_AndAreReported()
    {
        await using var connection = await E2EHarness.OpenConnectionAsync();
        await E2EHarness.CreateSchemaAsync(connection);
        var logs = new CapturingLoggerProvider();
        var stagedBy = new StagedWrites();
        await using var provider = BuildProvider(connection, logs, stagedBy);

        var envelope = new MessageEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Source = "tests.no-store",
            Type = ProofRequested.WireType,
            Time = DateTimeOffset.UtcNow,
            DataContentType = "application/json",
            Headers = new Dictionary<string, string>(StringComparer.Ordinal),
            Body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new ProofRequested(Guid.NewGuid()), JsonSerializerOptions.Web)),
        };
        var result = await provider.GetRequiredService<IEnvelopeReceiver>().ReceiveAsync(envelope);
        result.Disposition.ShouldBe(ReceiveDisposition.Acknowledge);

        await using var probe = E2EHarness.NewContext(connection);
        (await probe.Aggregates.AsNoTracking().CountAsync(a => a.Name == "saved-by-the-handler"))
            .ShouldBe(1, "the handler's own save committed");
        stagedBy.Runs.ShouldBe(0, "with no store, the bus dispatches nothing after the handler: nothing would save what it staged");
        (await probe.Aggregates.AsNoTracking().CountAsync(a => a.Name == "staged-by-an-in-transaction-handler"))
            .ShouldBe(0);
        logs.Entries.ShouldContain(e => e.EventId == 2310 && e.Level == LogLevel.Error && e.RenderedMessage.Contains(nameof(ProofRaised)),
            "a domain event still pending when the handler's scope ends is reported, never silently dropped");
    }

    private static ServiceProvider BuildProvider(SqliteConnection connection, CapturingLoggerProvider logs, StagedWrites staged)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        services.AddSingleton(staged);
        services.AddMicroKitPersistence(p => p
            .AddEntityFrameworkCore()
            .AddDbContext<E2EDbContext>(o => o.UseSqlite(connection))
            .AddUnitOfWork<E2EDbContext>());
        services.AddSingleton<HandlerInvocationRecorder>();
        services.AddScoped<IE2EWriter, EfE2EWriter>();
        services.AddMicroKitMediatR(cfg => cfg.FromAssemblyContaining<NoStoreDomainEventTests>().AddTransactionBehavior());
        services.AddMicroKitMessaging(bus =>
        {
            bus.ServiceName = "tests.no-store";
            bus.AddHandler<SaveThenRaiseHandler>();
            bus.UseMediatRDomainEvents();
        });
        return services.BuildServiceProvider();
    }

    [MessageContract("tests.no-store.proof-requested")]
    internal sealed record ProofRequested(Guid Id) : IIntegrationEvent
    {
        public const string WireType = "tests.no-store.proof-requested.v1";
    }

    internal sealed record ProofRaised(Guid Id, DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

    internal sealed class StagedWrites
    {
        private int _runs;

        public int Runs => Volatile.Read(ref _runs);

        public void Ran() => Interlocked.Increment(ref _runs);
    }

    /// <summary>
    /// Saves its aggregate through the unit of work, which drains no domain event, then raises one:
    /// before this fix the bus dispatched it after the handler, ran <see cref="StageOnProofRaised"/>,
    /// and nothing saved what it staged (measured: runs=1, persisted=0).
    /// </summary>
    internal sealed class SaveThenRaiseHandler(E2EDbContext db, IUnitOfWork unitOfWork) : IMessageHandler<ProofRequested>
    {
        public async ValueTask HandleAsync(ProofRequested message, MessageContext context, CancellationToken ct = default)
        {
            var aggregate = new E2EAggregate { Name = "saved-by-the-handler" };
            db.Aggregates.Add(aggregate);
            await unitOfWork.CommitAsync(ct);
            aggregate.Raise(new ProofRaised(message.Id, DateTimeOffset.UtcNow));
        }
    }

    /// <summary>An in-transaction (P2) handler: it stages a write and leaves the save to its caller.</summary>
    internal sealed class StageOnProofRaised(E2EDbContext db, StagedWrites staged) : IDomainEventHandler<ProofRaised>
    {
        public Task Handle(ProofRaised domainEvent, CancellationToken cancellationToken)
        {
            staged.Ran();
            db.Aggregates.Add(new E2EAggregate { Name = "staged-by-an-in-transaction-handler" });
            return Task.CompletedTask;
        }
    }
}
