using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace MicroKit.Messaging.IntegrationTests.Readme;

// The README's partitioned store (#191), compiled: every `// begin-snippet` region equals its README
// block (MicroKit.Messaging.UnitTests ReadmeSnippetTests), and PartitionedHostShapeTests runs it.

// begin-snippet: partitioned-source
// Which tenants have a database, and where. Declare a tenant once its database is migrated:
// the engine creates no table.
public sealed class TenantDatabases : IMessageStoreInstanceSource, IDisposable
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, string> _databases = new(StringComparer.Ordinal);
    private MessageStoreInstance[] _instances = [];
    private CancellationTokenSource _changed = new();

    public void Add(string tenant, string connectionString)
    {
        CancellationTokenSource changed;
        lock (_lock)
        {
            _databases[tenant] = connectionString;
            _instances = [.. _databases.Keys.Select(static id => new MessageStoreInstance(id))];
            (changed, _changed) = (_changed, new CancellationTokenSource());
        }

        changed.Cancel();   // the engine reads the new snapshot
        changed.Dispose();
    }

    public string ConnectionStringOf(string tenant)
    {
        lock (_lock)
        {
            return _databases[tenant];
        }
    }

    public IReadOnlyCollection<MessageStoreInstance> GetInstances()
    {
        lock (_lock)
        {
            return _instances;
        }
    }

    public IChangeToken GetChangeToken()
    {
        lock (_lock)
        {
            return new CancellationChangeToken(_changed.Token);
        }
    }

    public void Dispose() => _changed.Dispose();

    // A delivery's instance: the database of its tenant.
    public bool TryGetInstance(MessageContext context, [NotNullWhen(true)] out MessageStoreInstance? instance)
    {
        instance = context.TenantId is { } tenant ? new MessageStoreInstance(tenant) : null;
        return instance is not null;
    }
}

// The scope's connection, set before its DbContext is built.
public sealed class TenantConnection
{
    public string? ConnectionString { get; set; }
}

public sealed class TenantDatabaseInitializer(TenantConnection connection, TenantDatabases databases)
    : IMessageStoreInstanceInitializer
{
    public ValueTask InitializeAsync(MessageStoreInstance instance, IServiceProvider services, CancellationToken ct = default)
    {
        connection.ConnectionString = databases.ConnectionStringOf(instance.Id);
        return ValueTask.CompletedTask;
    }
}
// end-snippet

/// <summary>The application's context, as the README's durable composition declares it.</summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyMessagingConfiguration();
}

public static class ReadmeSnippets
{
    public static void PartitionedComposition(IServiceCollection services)
    {
        // begin-snippet: partitioned-composition
        services.AddScoped<TenantConnection>();
        services.AddDbContext<AppDbContext>((sp, o) => o.UseNpgsql(
            sp.GetRequiredService<TenantConnection>().ConnectionString
            ?? throw new InvalidOperationException("No tenant database in this scope.")));

        services.AddMicroKitMessaging(bus =>
        {
            bus.ServiceName = "shop.orders";
            bus.UseEntityFrameworkOutbox<AppDbContext>(store => store
                .PartitionedBy<TenantDatabases, TenantDatabaseInitializer>());
        });
        // end-snippet
    }
}
