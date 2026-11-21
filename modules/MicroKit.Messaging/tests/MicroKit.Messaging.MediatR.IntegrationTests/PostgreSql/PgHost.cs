using System.Text;
using System.Text.Json;

using MicroKit.MediatR.Behaviors.DependencyInjection;
using MicroKit.Messaging.IntegrationTests.PostgreSql;
using MicroKit.Messaging.MediatR.DependencyInjection;
using MicroKit.Persistence.EntityFrameworkCore;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MicroKit.Messaging.MediatR.IntegrationTests.PostgreSql;

/// <summary>
/// A hosted MicroKit.MediatR + MicroKit.Messaging host over PostgreSQL, with <b>no transport at
/// all</b> (#188 D7): store A (<see cref="PgOrdersContext"/>, the commands' unit of work) and,
/// optionally, store B (<see cref="PgBillingContext"/>) bound to this assembly.
/// </summary>
/// <remarks>
/// Started for real, so the composition checks run (D12, C3). The store engine's loops are removed:
/// each test drives every outbox pass and retry pass itself, so the attempt counts it asserts are
/// the ones it caused. The loops' own behaviour is V5's and the core suite's.
/// </remarks>
internal sealed class PgHost : IAsyncDisposable
{
    public const string ServiceName = "tests.pg.domain-events";

    public static readonly MessageStoreKey StoreA = EntityFrameworkOutbox.KeyOf<PgOrdersContext>();
    public static readonly MessageStoreKey StoreB = EntityFrameworkOutbox.KeyOf<PgBillingContext>();

    private readonly IHost _host;
    private readonly StoreDatabase _a;
    private readonly StoreDatabase? _b;

    private PgHost(IHost host, StoreDatabase a, StoreDatabase? b)
    {
        _host = host;
        _a = a;
        _b = b;
    }

    public IServiceProvider Services => _host.Services;

    public PgProbe Probe => Services.GetRequiredService<PgProbe>();

    public CapturingLoggerProvider Logs => Services.GetRequiredService<CapturingLoggerProvider>();

    /// <summary>Creates the databases and the host, and starts it.</summary>
    /// <param name="server">The PostgreSQL server.</param>
    /// <param name="twoStores">Whether store B is configured, bound to this assembly.</param>
    /// <param name="bus">Further bus registrations: handlers.</param>
    public static async Task<PgHost> StartAsync(string server, bool twoStores = false, Action<MessagingBuilder>? bus = null)
    {
        var a = await StoreDatabase.CreateAsync<PgOrdersContext>(server, o => new PgOrdersContext(o));
        var b = twoStores ? await StoreDatabase.CreateAsync<PgBillingContext>(server, o => new PgBillingContext(o)) : null;

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        var logs = new CapturingLoggerProvider();
        builder.Logging.SetMinimumLevel(LogLevel.Debug).AddProvider(logs);
        builder.Services.AddSingleton(logs);
        builder.Services.AddSingleton<PgProbe>();

        builder.Services.AddMicroKitPersistence(p => p
            .AddEntityFrameworkCore()
            .AddDbContext<PgOrdersContext>(o => o.UseNpgsql(a.ConnectionString))
            .AddUnitOfWork<PgOrdersContext>());
        if (b is not null)
        {
            builder.Services.AddDbContext<PgBillingContext>(o => o.UseNpgsql(b.ConnectionString));
        }

        builder.Services.AddMicroKitMediatR(cfg => cfg
            .FromAssemblyContaining<PgSettleOrderCommand>()
            .AddTransactionBehavior());

        builder.Services.AddMicroKitMessaging(messaging =>
        {
            messaging.ServiceName = ServiceName;
            if (b is null)
            {
                messaging.UseEntityFrameworkOutbox<PgOrdersContext>();
            }
            else
            {
                // A binds nothing; every handler and notification of this assembly is B's.
                messaging.UseEntityFrameworkOutbox<PgOrdersContext>();
                messaging.UseEntityFrameworkOutbox<PgBillingContext>(store => store.ForAssemblyContaining<PgSettleOrderCommand>());
            }

            messaging.UseMediatRDomainEvents();
            bus?.Invoke(messaging);
            messaging.Configure(o =>
            {
                // Due at once: a test drives each retry pass itself.
                o.Inbox.MaxRetryBackoff = TimeSpan.FromTicks(1);
                o.Outbox.RetentionDays = 0;
                o.Inbox.RetentionDays = 0;
            });
        });

        // No loop: the test drives each pass.
        var engine = builder.Services.Single(d => d.ImplementationType == typeof(Processing.MessageStoreEngine));
        builder.Services.Remove(engine);

        var host = builder.Build();
        await host.StartAsync();
        return new PgHost(host, a, b);
    }

    /// <summary>Sends a command through MediatR, in a scope of its own, as a request would.</summary>
    public async Task SendAsync<TCommand>(TCommand command)
        where TCommand : ICommand<Result<Guid>>
    {
        await using var scope = Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IMediator>().SendCommandAsync<TCommand, Result<Guid>>(command);
        result.IsSuccess.ShouldBeTrue();
    }

    /// <summary>One pass of a store's outbox loop: local rows are delivered, one guarded delivery per handler.</summary>
    public async Task DrainAsync(MessageStoreKey? store = null)
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredKeyedService<Processing.IOutboxProcessor>(store ?? StoreA).ProcessBatchAsync(100);
    }

    /// <summary>One pass of a store's retry loop.</summary>
    public async Task RetryAsync(MessageStoreKey? store = null)
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredKeyedService<Processing.IInboxProcessor>(store ?? StoreA).ProcessBatchAsync(100);
    }

    /// <summary>Hands an envelope to the receiving seam, as a broker provider's consume loop would.</summary>
    public ValueTask<EnvelopeReceiveResult> ReceiveAsync(Guid messageId, PgPaymentReceived message)
        => Services.GetRequiredService<IEnvelopeReceiver>().ReceiveAsync(new MessageEnvelope
        {
            Id = messageId.ToString(),
            Source = "tests.pg.payments",
            Type = PgPaymentReceived.WireType,
            Time = DateTimeOffset.UtcNow,
            DataContentType = "application/json",
            Headers = new Dictionary<string, string>(StringComparer.Ordinal),
            Body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, JsonSerializerOptions.Web)),
        });

    public PgOrdersContext NewA() => new(new DbContextOptionsBuilder<PgOrdersContext>().UseNpgsql(_a.ConnectionString).Options);

    public PgBillingContext NewB() => new(new DbContextOptionsBuilder<PgBillingContext>().UseNpgsql(_b!.ConnectionString).Options);

    public async Task<List<string>> EffectsAsync()
    {
        await using var context = NewA();
        return await context.Effects.AsNoTracking().Select(e => e.Value).ToListAsync();
    }

    public async Task<List<InboxMessage>> InboxAsync()
    {
        await using var context = NewA();
        return await context.InboxMessages.AsNoTracking().ToListAsync();
    }

    public async Task<List<OutboxMessage>> OutboxAsync()
    {
        await using var context = NewA();
        return await context.OutboxMessages.AsNoTracking().ToListAsync();
    }

    /// <summary>Puts a dispatched row back to Pending: a crash between its dispatch and its settlement.</summary>
    public async Task ReplayAsync(MessageId row)
    {
        await using var context = NewA();
        (await context.OutboxMessages.Where(m => m.Id == row).ExecuteUpdateAsync(s => s
            .SetProperty(m => m.Status, OutboxMessageStatus.Pending)
            .SetProperty(m => m.ProcessedAtUtc, (DateTimeOffset?)null))).ShouldBe(1);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _host.StopAsync();
        }
        finally
        {
            _host.Dispose();
            await _a.DisposeAsync();
            if (_b is not null)
            {
                await _b.DisposeAsync();
            }
        }
    }
}
