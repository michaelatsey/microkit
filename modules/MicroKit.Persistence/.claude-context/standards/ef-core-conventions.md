# Standard: EF Core Conventions

**Canonical conventions for `MicroKit.Persistence.EntityFrameworkCore` and provider projects.**

---

## DbContext Design

```csharp
// ✅ One DbContext per module — never cross-module DbContext
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Apply all IEntityTypeConfiguration<T> from this assembly
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
```

---

## Entity Configuration

```csharp
// ✅ One IEntityTypeConfiguration<T> per aggregate — never inline OnModelCreating
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);

        // Strongly-typed ID value converter
        builder.Property(u => u.Id)
               .HasConversion(id => id.Value, v => new UserId(v));

        // Owned value object
        builder.OwnsOne(u => u.Email, eb =>
        {
            eb.Property(e => e.Value).HasColumnName("Email").HasMaxLength(320).IsRequired();
        });

        // Discriminated union via owned entity
        builder.Property(u => u.Status)
               .HasConversion<string>()
               .HasMaxLength(50);
    }
}
```

---

## Tracking Behavior

| Context | Tracking | Setting |
|---------|----------|---------|
| Write (command handlers) | YES | Default (`TrackAll`) |
| Read (query handlers) | NO | `AsNoTracking()` per query or `UseQueryTrackingBehavior(NoTracking)` |
| Integration tests | Context-dependent | Set per test class |

Recommended default: tracking ON in the DbContext, `AsNoTracking()` applied per query in
`IReadRepository` implementations (explicit is better than implicit).

---

## Value Converters for Strongly-Typed IDs

```csharp
// ✅ Registered per entity in configuration class
builder.Property(u => u.Id)
       .HasConversion(id => id.Value, v => new UserId(v));

// ✅ Or via a global convention (for bulk registration)
protected override void ConfigureConventions(ModelConfigurationBuilder cfg)
{
    cfg.Properties<UserId>().HaveConversion<UserIdConverter>();
}
```

---

## Naming Conventions

### SQL Server (default — PascalCase)
No convention registration needed — EF Core defaults to PascalCase table/column names.

### PostgreSQL (Npgsql — snake_case)
```csharp
protected override void ConfigureConventions(ModelConfigurationBuilder cfg)
{
    // Tables and columns follow snake_case
    cfg.Properties<string>().HaveMaxLength(500); // sensible default
}

// Or use NpgsqlSnakeCaseNameRewriter from EFCore.NamingConventions
optionsBuilder.UseSnakeCaseNamingConvention();
```

---

## Migration Conventions

```bash
# Names: PascalCase, descriptive
dotnet ef migrations add AddUserAuditColumns
dotnet ef migrations add CreateOrdersTable
dotnet ef migrations add AddIndexOnUserEmail

# Never use generic names
dotnet ef migrations add Fix         # ❌ — what fix?
dotnet ef migrations add Update      # ❌ — what update?
```

Migration files must be reviewed for:
- Destructive column drops (must be coordinated with feature flag or multi-step migration)
- `NONCLUSTERED INDEX` missing on foreign keys (SQL Server)
- Missing `IS NOT NULL` on new required columns without a default value (will fail on existing rows)

---

## DbContext Registration

```csharp
// ✅ Using the EfCoreBuilder fluent API
services.AddMicroKitPersistence(persistence =>
    persistence.AddEntityFrameworkCore(ef =>
        ef.UsePostgreSQL(connectionString, opts =>
            opts.MigrationsAssembly("MyApp.Migrations"))));
```
