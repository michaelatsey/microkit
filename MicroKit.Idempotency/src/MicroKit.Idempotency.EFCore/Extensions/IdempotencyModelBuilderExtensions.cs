using MicroKit.Idempotency.EFCore.Configurations;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Idempotency.EFCore.Extensions;

/// <summary>Extension methods for applying MicroKit idempotency EF Core entity configurations.</summary>
public static class IdempotencyModelBuilderExtensions
{
    /// <summary>Applies the idempotency entity type configurations for the detected database provider.</summary>
    /// <param name="modelBuilder">The EF Core model builder.</param>
    /// <param name="context">The <see cref="DbContext"/> used to determine the active provider.</param>
    /// <returns>The same <paramref name="modelBuilder"/> for chaining.</returns>
    public static ModelBuilder ApplyMicroKitIdempotencyConfigurations(this ModelBuilder modelBuilder, DbContext context)
    {
        // On récupère le nom du provider (SqlServer, Npgsql, etc.)
        var providerName = context.Database.ProviderName;

        // On applique les configurations internes
        modelBuilder.ApplyConfiguration(new IdempotencyRecordConfiguration(providerName));

        return modelBuilder;
    }
}
