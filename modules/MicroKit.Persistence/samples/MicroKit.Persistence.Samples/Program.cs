using MicroKit.Persistence.Samples;
using Microsoft.EntityFrameworkCore;

// The samples are compiled; three smoke checks run. The README models build with MicroKit's boundary
// validation (no connection is opened), the in-memory doubles behave as documented, and — when a Docker
// daemon answers — the relational fixture starts PostgreSQL and an order round-trips through it.
await using (var context = new ShopDbContext(ContextOptions.ForShop("Host=localhost;Database=unused")))
{
    _ = context.Model;
}

await using (var context = new CatalogDbContext(
    new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql("Host=localhost;Database=unused").UseMicroKitPersistence().Options))
{
    _ = context.Model;
}

await using (var context = new BillingDbContext(
    new DbContextOptionsBuilder<BillingDbContext>().UseNpgsql("Host=localhost;Database=unused").UseMicroKitPersistence().Options))
{
    _ = context.Model;
}

await TestingSample.RunAsync();
await ResultTestingSample.RunAsync();

// Exit code 0 either way: without Docker the sample returns false, as a test would skip. Testcontainers
// logs the container it starts and removes.
_ = await RelationalTestingSample.RunAsync();
