using MicroKit.Persistence.Samples;
using Microsoft.EntityFrameworkCore;

// The samples are compiled, not run against a database. Two smoke checks run instead: the README
// models build with MicroKit's boundary validation (no connection is opened), and the in-memory
// doubles behave as documented.
await using (var context = new ShopDbContext(ContextOptions.ForShop("Host=localhost;Database=unused")))
{
    _ = context.Model;
}

await using (var context = new CatalogDbContext(
    new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql("Host=localhost;Database=unused").UseMicroKitPersistence().Options))
{
    _ = context.Model;
}

await TestingSample.RunAsync();
await ResultTestingSample.RunAsync();
