namespace MicroKit.Persistence.Samples;

public static class Composition
{
    // README: "Composition"
    public static IServiceCollection AddShopPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddMicroKitPersistence(persistence => persistence
            .AddEntityFrameworkCore(ef => ef
                .UsePostgreSQL<ShopDbContext>(connectionString, options: new ProviderOptions
                {
                    Retry = new RetryOnFailureOptions(maxRetryCount: 5),
                })
                .AddUnitOfWork<ShopDbContext>()
                .ConfigurePagination(new PaginationLimits(defaultPageSize: 20, maxPageSize: 100))));

        services.AddScoped<IOrderRepository, EfOrderRepository>();
        services.AddScoped<IReadRepository<Order>, EfReadRepository<Order, ShopDbContext>>();
        return services;
    }
}
