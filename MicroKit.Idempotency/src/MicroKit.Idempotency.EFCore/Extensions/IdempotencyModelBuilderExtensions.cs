using MicroKit.Idempotency.EFCore.Configurations;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Idempotency.EFCore.Extensions;

public static class IdempotencyModelBuilderExtensions
{
    public static ModelBuilder ApplyMicroKitIdempotencyConfigurations(this ModelBuilder modelBuilder, DbContext context)
    {
        // On récupère le nom du provider (SqlServer, Npgsql, etc.)
        var providerName = context.Database.ProviderName;

        // On applique les configurations internes
        modelBuilder.ApplyConfiguration(new IdempotencyRecordConfiguration(providerName));

        return modelBuilder;
    }
}
