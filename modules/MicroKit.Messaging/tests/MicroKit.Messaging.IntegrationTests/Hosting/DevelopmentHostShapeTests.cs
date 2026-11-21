using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using MicroKit.Messaging.IntegrationTests.SecondModule;

namespace MicroKit.Messaging.IntegrationTests.Hosting;

/// <summary>
/// Each host shape the README shows builds under the Development defaults, which turn on
/// <c>ValidateOnBuild</c> and <c>ValidateScopes</c>: a registration whose dependency a valid
/// composition may not provide must not fail <c>Build()</c>. The MediatR shape is in
/// <c>MicroKit.Messaging.MediatR.IntegrationTests</c>.
/// </summary>
/// <remarks>
/// Every other host in the suites sets <c>DisableDefaults = true</c>, which is how a typed
/// registration of the store's transport dispatcher reached review (#188 P4).
/// </remarks>
public sealed class DevelopmentHostShapeTests
{
    private const string Sqlite = "DataSource=:memory:";

    /// <summary>The Development defaults, exactly as a host started in Development gets them.</summary>
    private static HostApplicationBuilder Development()
        => Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development,
            ApplicationName = "tests.host-shapes",
        });

    [Fact]
    public void NoStore_WithTheInMemoryTransport_Builds()
    {
        var builder = Development();
        builder.Services.AddMicroKitMessaging(bus =>
        {
            bus.Publishes<ShapeEvent>();
            bus.AddHandler<ShapeHandler>();
            bus.UseInMemoryTransport();
        });

        using var host = builder.Build();
    }

    [Fact]
    public void AStore_WithATransport_Builds()
    {
        var builder = Development();
        builder.Services.AddDbContext<TestMessagingDbContext>(o => o.UseSqlite(Sqlite));
        builder.Services.AddMicroKitMessaging(bus =>
        {
            bus.Publishes<ShapeEvent>();
            bus.AddHandler<ShapeHandler>();
            bus.UseEntityFrameworkOutbox<TestMessagingDbContext>();
            bus.UseInMemoryTransport();
        });

        using var host = builder.Build();
    }

    [Fact]
    public void AStore_WithNoTransport_Builds()
    {
        // The README's durable composition for a host whose only messages are domain events.
        var builder = Development();
        builder.Services.AddDbContext<TestMessagingDbContext>(o => o.UseSqlite(Sqlite));
        builder.Services.AddMicroKitMessaging(bus => bus.UseEntityFrameworkOutbox<TestMessagingDbContext>());

        using var host = builder.Build();
    }

    [Fact]
    public void TwoStores_EachBound_Builds()
    {
        var builder = Development();
        builder.Services.AddDbContext<TestMessagingDbContext>(o => o.UseSqlite(Sqlite));
        builder.Services.AddDbContext<SecondMessagingDbContext>(o => o.UseSqlite(Sqlite));
        builder.Services.AddSingleton<SecondModuleRecorder>();
        builder.Services.AddMicroKitMessaging(bus =>
        {
            bus.Publishes<ShapeEvent>();
            bus.AddHandler<ShapeHandler>();
            bus.AddHandler<SecondModuleHandler>();
            bus.UseEntityFrameworkOutbox<TestMessagingDbContext>(store => store.ForAssemblyContaining<DevelopmentHostShapeTests>());
            bus.UseEntityFrameworkOutbox<SecondMessagingDbContext>(store => store.ForAssemblyContaining<SecondModuleMarker>());
            bus.UseInMemoryTransport();
        });

        using var host = builder.Build();
    }
}

[MessageContract("tests.host-shapes.event")]
public sealed record ShapeEvent(Guid Id) : IIntegrationEvent;

public sealed class ShapeHandler : IMessageHandler<ShapeEvent>
{
    public ValueTask HandleAsync(ShapeEvent message, MessageContext context, CancellationToken ct = default)
        => ValueTask.CompletedTask;
}
