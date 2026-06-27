namespace MicroKit.Tenancy.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore.Metadata.Builders;

/// <summary>
/// EF Core entity type configuration for <see cref="EfTenantRecord"/>.
/// Applied automatically by <see cref="TenantStoreDbContext"/>.
/// </summary>
/// <remarks>
/// Provides table name, primary key, and column length constraints to prevent unbounded
/// string columns in production databases.
/// </remarks>
public sealed class EfTenantRecordConfiguration : IEntityTypeConfiguration<EfTenantRecord>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<EfTenantRecord> builder)
    {
        builder.ToTable("Tenants");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .ValueGeneratedNever();

        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(t => t.ConnectionString)
            .HasMaxLength(2048);

        builder.Property(t => t.SchemaName)
            .HasMaxLength(128);

        builder.Property(t => t.IsActive)
            .IsRequired();

        builder.HasIndex(t => t.Name)
            .IsUnique();
    }
}
