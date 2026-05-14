using MicroKit.Abstractions.Configuration;
using MicroKit.Core;
using MicroKit.Cqrs.Abstractions.Commands;
using MicroKit.Cqrs.Abstractions.Queries;
using MicroKit.Cqrs.MediatR.Commands;
using MicroKit.Cqrs.MediatR.Queries;
using MicroKit.Caching.Distributed;
using MicroKit.Messaging.Core.Extensions;
using MicroKit.Messaging.Core.Extensions.Outbox;
using MicroKit.Idempotency.Core;
using MicroKit.Idempotency.MediatR;
using MicroKit.Idempotency.EFCore;
using MicroKit.MultiTenancy.Extensions;
using MicroKit.Resilience;
using MicroKit.Resilience.MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderApi.Domain;
using OrderApi.Domain.Orders.Repositories;
using OrderApi.Infrastructure.Persistence;
using OrderApi.Infrastructure.Persistence.Repositories;
using MicroKit.Messaging.Persistence.EFCore.Extensions;
using MicroKit.Messaging.Publisher.MediatR.Extensions;

namespace OrderApi.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' not found.");

        services.AddDbContext<AppDbContext>(opts =>
            opts.UseNpgsql(connectionString));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IOrderRepository, OrderRepository>();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblies(
                typeof(DependencyInjection).Assembly,
                typeof(OrderApi.Application.Orders.Handlers.PlaceOrderHandler).Assembly);
        });

        services.AddScoped<ICommandBus, MediatRCommandBus>();
        services.AddScoped<IQueryBus, MediatRQueryBus>();

        var microkit = services.AddMicroKit();

        microkit
            .AddMicroKitMessaging(msg => msg
                .UseOutbox()
                .UseEfCorePersistence<AppDbContext>()
                .UseMediatRPublisher());

        microkit
            .AddMicroKitIdempotency(idp => idp
                .UseMediatRPipeline()
                .UseEFcore<AppDbContext>());

        microkit.AddMicroKitDistributedCache();

        services.AddDistributedMemoryCache();

        services.AddMicroKitMultiTenancy()
            .WithHeaderStrategy("X-Tenant-Id")
            .WithInMemoryCache();

        services.AddMicroKitResilience()
            .AddMicroKitResilienceMediatR();

        return services;
    }
}
