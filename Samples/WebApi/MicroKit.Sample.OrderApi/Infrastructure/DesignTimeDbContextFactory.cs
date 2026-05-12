using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MicroKit.Sample.OrderApi.Infrastructure;

/// <summary>Design-time factory for creating <see cref="ApplicationDbContext"/> during EF Core migrations.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    /// <inheritdoc/>
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

        // Configuration pour les migrations
        var connectionString = "Server=.\\SQLEXPRESS;Database=MicroKitSampleOrderApi_Db;TrustServerCertificate=True;Trusted_Connection=True;Integrated Security=True";
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();

        optionsBuilder.UseSqlServer(connectionString, sqlServerOptions =>
        {
            // Point critique : spécifier l'assembly des migrations
            sqlServerOptions.MigrationsAssembly(typeof(DesignTimeDbContextFactory).Assembly.FullName);
            // sqlServerOptions.MigrationsAssembly(Assembly.GetExecutingAssembly().FullName);
            sqlServerOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null);
        });

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
