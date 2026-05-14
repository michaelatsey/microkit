using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderApi.Domain.Orders;
using OrderApi.Domain.Orders.ValueObjects;

namespace OrderApi.Infrastructure.Persistence.Configurations;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id");
        builder.Property(o => o.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(o => o.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.Property(o => o.Status).HasColumnName("status").HasConversion<string>();
        builder.Property(o => o.PlacedAt).HasColumnName("placed_at");
        builder.Property(o => o.ConfirmedAt).HasColumnName("confirmed_at");
        builder.Property(o => o.CancelledAt).HasColumnName("cancelled_at");
        builder.Property(o => o.Version).HasColumnName("version").IsConcurrencyToken();

        builder.OwnsOne(o => o.TotalAmount, m =>
        {
            m.Property(x => x.Amount).HasColumnName("total_amount");
            m.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3);
        });

        builder.OwnsMany(o => o.Items, items =>
        {
            items.ToJson("items");
            items.Property(i => i.ProductId).HasJsonPropertyName("productId");
            items.Property(i => i.ProductName).HasJsonPropertyName("productName");
            items.Property(i => i.Quantity).HasJsonPropertyName("quantity");
            items.OwnsOne(i => i.UnitPrice, price =>
            {
                price.Property(p => p.Amount).HasJsonPropertyName("amount");
                price.Property(p => p.Currency).HasJsonPropertyName("currency");
            });
        });
    }
}
