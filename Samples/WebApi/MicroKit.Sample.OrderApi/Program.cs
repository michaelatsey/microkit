
using Autofac;
using MicroKit.Core;
using MicroKit.Core.Extensions.Serialization;
using MicroKit.Cqrs;
using MicroKit.Cqrs.MediatR.Autofac;
using MicroKit.Cqrs.MediatR.Caching;
using MicroKit.Cqrs.MediatR.Caching.Pipelines;
using MicroKit.Idempotency.Core;
using MicroKit.Idempotency.EFCore;
using MicroKit.Idempotency.MediatR;
using MicroKit.Idempotency.MediatR.Behaviors;
using MicroKit.Messaging.Core.Extensions;
using MicroKit.Messaging.Core.Extensions.Inbox;
using MicroKit.Messaging.Core.Extensions.Outbox;
using MicroKit.Messaging.Persistence.EFCore.Extensions;
using MicroKit.Messaging.Publisher.MediatR.Extensions;
using MicroKit.MultiTenancy.Abstractions;
using MicroKit.MultiTenancy.EFCoreStore;
using MicroKit.MultiTenancy.Extensions;
using MicroKit.MultiTenancy.Stores;
using MicroKit.Sample.OrderApi.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using System;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Order API - MicroKit Sample",
        Version = "v1",
        Description = "Test du syst�me Outbox/Inbox"
    });
});
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
});

// Fournit un DbContext SCOPED bas� sur la factory
builder.Services.AddScoped(sp =>
{
    var factory = sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
    return factory.CreateDbContext();
});

builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

builder.Services
    .AddMicroKit()
    .AddSystemTextJson()
    .AddMicroKitMessaging(config =>
    {
        config
            .UseOutbox(options =>
            {
                //options.PollingInterval = TimeSpan.FromMinutes(5);
                options.CleanupRunInterval = null; // D�sactive le nettoyage automatique de l'outbox
            })
            .UseInbox()
            .UseEfCorePersistence<ApplicationDbContext>()
            .UseMediatRPublisher();
    })
    .AddMicroKitIdempotency(builder =>
    {
        builder.Configure(options =>
        {
            options.CleanupRunInterval = TimeSpan.FromMinutes(10);
        });
        builder.UseMediatRPipeline();
        builder.UseEFcore<ApplicationDbContext>();
    });


builder.Services
    .AddMicroKitMultiTenancy()
    .WithHeaderStrategy()
    //.WithDatabaseStore<ApplicationDbContext>(
    //    options => {
    //        options.CacheExpirationMinutes = TimeSpan.FromMinutes(5);
    //    },
    //    services => {
    //        // Ici, le d�veloppeur d�cide que pour ce store sp�cifique, 
    //        // il veut un registre diff�rent (ex: Redis ou Statique)
    //        //services.AddScoped<ITenantRegistry, CustomDeveloperRegistry>();
    //    }
    //)
    //.WithRemoteStore(options =>
    //{
    //    options.BaseAddress = new Uri("https://identity-service.internal");
    //    options.RoutePattern = "v1/organizations/lookup/{0}/details";
    //    options.Timeout = TimeSpan.FromSeconds(2);
    //    options.CacheExpirationMinutes = TimeSpan.FromSeconds(2);
    //})
    .WithInMemoryCache();

builder.Services.AddHttpClient("TenantService", (sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<RemoteTenantOptions>>().Value;
    client.Timeout = options.Timeout;
});

builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());


//builder.Host.ConfigureContainer<ContainerBuilder>(container =>
//{
//    container.AddMicroKitCqrs(builder =>
//    {
//        builder.Configure(options =>
//        {
//            // Scan d'assemblies et enregistrements avanc�s
//            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
//            options.AddAssemblies([.. assemblies]);
//        });
//        builder.UseMediatRModule(mediatrCfg =>
//        {
//            mediatrCfg.UseDistributedCache();

//            mediatrCfg.AddPipeline(typeof(SecurityContextBehavior<,>), -200);
//            mediatrCfg.AddPipeline(typeof(ValidationBehavior<,>), -150);
//            mediatrCfg.AddPipeline(typeof(LoggingBehavior<,>), -100);
//            mediatrCfg.AddPipeline(typeof(ResilienceBehavior<,>), 10);
//            mediatrCfg.AddPipeline(typeof(CachingBehavior<,>), 20); // 
//            mediatrCfg.AddPipeline(typeof(TransactionBehavior<,>), 30);
//            mediatrCfg.AddPipeline(typeof(IdempotencyBehavior<,>), 500);
//            mediatrCfg.AddPipeline(typeof(CacheInvalidationBehavior<,>), 1000);�
//        });
//    });
//});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger(options =>
    {
        options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_1;
    });
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Order API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseMicroKitMultiTenancy();

app.UseAuthorization();

app.MapControllers();

app.Run();
