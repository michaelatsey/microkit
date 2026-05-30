# Template: EF Core Entity Configuration

Use this template for every new `IEntityTypeConfiguration<T>` in the EntityFrameworkCore project.

---

## Basic Configuration

```csharp
namespace MicroKit.Persistence.EntityFrameworkCore.Configurations;

/// <summary>
/// EF Core entity type configuration for <see cref="{Entity}"/>.
/// </summary>
internal sealed class {Entity}Configuration : IEntityTypeConfiguration<{Entity}>
{
    public void Configure(EntityTypeBuilder<{Entity}> builder)
    {
        // ── Table ────────────────────────────────────────────────────────────
        builder.ToTable("{Entities}");

        // ── Primary Key ──────────────────────────────────────────────────────
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
               .HasConversion(id => id.Value, v => new {Entity}Id(v))
               .ValueGeneratedNever();   // IDs generated in the domain

        // ── Properties ───────────────────────────────────────────────────────
        builder.Property(e => e.Name)
               .IsRequired()
               .HasMaxLength(200);

        builder.Property(e => e.Status)
               .HasConversion<string>()
               .HasMaxLength(50)
               .IsRequired();

        // ── Owned Value Objects ───────────────────────────────────────────────
        builder.OwnsOne(e => e.Email, eb =>
        {
            eb.Property(x => x.Value)
              .HasColumnName("Email")
              .HasMaxLength(320)
              .IsRequired();
        });

        // ── Audit Columns ─────────────────────────────────────────────────────
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.UpdatedAt).IsRequired(false);

        // ── Relations ─────────────────────────────────────────────────────────
        builder.HasMany(e => e.Items)
               .WithOne()
               .HasForeignKey("{Entity}Id")
               .OnDelete(DeleteBehavior.Cascade);

        // ── Indexes ───────────────────────────────────────────────────────────
        builder.HasIndex(e => e.Email)
               .IsUnique()
               .HasDatabaseName("IX_{Entities}_Email");

        // ── Soft Delete (if applicable) ───────────────────────────────────────
        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}
```

---

## Child Entity Configuration (Owned Type)

```csharp
// For value objects owned by the aggregate — no separate table
builder.OwnsOne(e => e.Address, ab =>
{
    ab.Property(a => a.Street).HasColumnName("Street").HasMaxLength(200);
    ab.Property(a => a.City).HasColumnName("City").HasMaxLength(100);
    ab.Property(a => a.PostalCode).HasColumnName("PostalCode").HasMaxLength(20);
    ab.Property(a => a.CountryCode).HasColumnName("CountryCode").HasMaxLength(2);
});
```

---

## Value Converter Pattern

```csharp
// Inline (preferred for one-off conversions)
builder.Property(e => e.Id)
       .HasConversion(id => id.Value, v => new {Entity}Id(v));

// Reusable (for project-wide conventions in ConfigureConventions)
public sealed class {Entity}IdConverter()
    : ValueConverter<{Entity}Id, Guid>(id => id.Value, v => new {Entity}Id(v));
```
