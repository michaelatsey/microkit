using MicroKit.Sample.OrderApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;

namespace MicroKit.Sample.OrderApi.Infrastructure.Configuratio;

/// <summary>EF Core entity type configuration for <see cref="Order"/>.</summary>
public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id)
            .ValueGeneratedNever(); // On le génère nous-mêmes avec Guid.NewGuid()

        builder.Property(o => o.OrderDate)
            .IsRequired();

        builder.Property(o => o.TotalAmount)
            .HasPrecision(18, 2) // Important pour les montants financiers
            .IsRequired();

        // Stockage des ProductIds sous forme de JSON (string) en base de données
        builder.Property(o => o.ProductId)
            .IsRequired();

        // Note : Si tu veux optimiser la recherche par produit plus tard, 
        // il faudra passer par une table de jointure réelle.
    }
}
