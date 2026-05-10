
using MicroKit.Messaging.Core.Extensions;
using MicroKit.Messaging.Core.Extensions.Inbox;
using MicroKit.Messaging.Core.Extensions.Outbox;
using MicroKit.Messaging.Persistence.EFCore.Extensions;
using MicroKit.Messaging.Publisher.MediatR.Extensions;
using MicroKit.Sample.OrderApi.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using MicroKit.Idempotency.Core;
using MicroKit.Idempotency.EFCore.Stores;
using MicroKit.Core.Extensions.Serialization;
using MicroKit.Core;
using MicroKit.Idempotency.EFCore;


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
        Description = "Test du système Outbox/Inbox"
    });
});
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
});

// Fournit un DbContext SCOPED basé sur la factory
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
                options.CleanupRunInterval = null; // Désactive le nettoyage automatique de l'outbox
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
        builder.UseEFcore<ApplicationDbContext>();
    });

builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());

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

app.UseAuthorization();

app.MapControllers();

app.Run();
