using MicroKit.MediatR.Behaviors.DependencyInjection;
using MicroKit.Messaging.MediatR.DependencyInjection;
using MicroKit.Persistence.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace MicroKit.Messaging.MediatR.IntegrationTests;

/// <summary>
/// The README's durable composition — a store, MicroKit.MediatR, <c>UseMediatRDomainEvents()</c>,
/// no transport — builds under the Development defaults (<c>ValidateOnBuild</c>, <c>ValidateScopes</c>).
/// The four bus shapes are in <c>MicroKit.Messaging.IntegrationTests</c>.
/// </summary>
/// <remarks>
/// MediatR scans an assembly with no handler (<c>MicroKit.Messaging</c>'s), and the one notification
/// handler is registered by hand: <c>ValidateOnBuild</c> validates every registered handler, and this
/// assembly's other fixtures need services this host does not register.
/// </remarks>
public sealed class DevelopmentHostShapeTests
{
    [Fact]
    public void AStore_WithMediatRDomainEvents_AndNoTransport_Builds()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development,
            ApplicationName = "tests.host-shapes.mediatr",
        });
        builder.Services.AddSingleton<HandlerInvocationRecorder>();
        builder.Services.AddMicroKitPersistence(p => p
            .AddEntityFrameworkCore()
            .AddDbContext<E2EDbContext>(o => o.UseSqlite("DataSource=:memory:"))
            .AddUnitOfWork<E2EDbContext>());
        builder.Services.AddMicroKitMediatR(cfg => cfg
            .FromAssemblyContaining<MessagingBuilder>()
            .AddTransactionBehavior());
        builder.Services.AddTransient<INotificationHandler<WidgetCreatedNotification>, RecordWidgetCreatedHandler>();
        builder.Services.AddMicroKitMessaging(bus => bus
            .UseEntityFrameworkOutbox<E2EDbContext>()
            .UseMediatRDomainEvents());

        using var host = builder.Build();
    }
}
