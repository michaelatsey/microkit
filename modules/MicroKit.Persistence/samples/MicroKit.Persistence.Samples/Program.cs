using MicroKit.Persistence.Samples;
using Microsoft.EntityFrameworkCore;

// The samples are compiled, not run against a database. Two smoke checks run instead: the README
// model builds (no connection is opened), and the in-memory doubles behave as documented.
await using (var context = new ShopDbContext(
    new DbContextOptionsBuilder<ShopDbContext>().UseNpgsql("Host=localhost;Database=unused").Options))
{
    _ = context.Model;
}

await TestingSample.RunAsync();
