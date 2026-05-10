using MicroKit.Idempotency.EFCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MicroKit.Idempotency.EFCore.Configurations;

public class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    private readonly string? _providerName;

    public IdempotencyRecordConfiguration(string? providerName)
    {
        _providerName = providerName;
    }
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("MicroKit_IdempotencyRecords");

        builder.HasKey(x => x.Key);
        builder.Property(x => x.Key)
            .ValueGeneratedNever();

        builder.Property(x => x.TenantId)
            .IsRequired()
            .HasMaxLength(256);        

        builder.Property(x => x.RequestHash)
            .IsRequired(false)
            .HasMaxLength(128);

        builder.Property(x => x.Response)
            .IsRequired(false)
            .HasColumnType("nvarchar(max)");

        builder.Property(ts => ts.Status)
            .IsRequired();

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.Property(x => x.CompletedAtUtc)
            .IsRequired(false);

        builder.Property(e => e.ExpiresAtUtc)
            .IsRequired(false);

        builder.Property(x => x.RowVersion)
            .IsRowVersion();

        // Unique index on Key and TenantId to ensure uniqueness across tenants
        builder.HasIndex(x => new { x.Key, x.TenantId })
            .IsUnique()
            .HasDatabaseName("UX_Idempotency_Key_Tenant");

        builder.HasIndex(x => new { x.Status, x.CompletedAtUtc })
            .HasDatabaseName("IX_Idempo_Status_CompletedAt");

        builder.HasIndex(x => new { x.Status, x.ExpiresAtUtc })
            .HasDatabaseName("IX_Idempo_Status_ExpiresAt");

        // Index on ExpiresAtUtc for efficient cleanup of expired records
        builder.HasIndex(e => e.ExpiresAtUtc)
            .HasDatabaseName("IX_Idempotency_ExpiresAt");

        builder.HasIndex(x => x.Status)
            .HasDatabaseName("IX_Idempotency_Status");


    }
}
