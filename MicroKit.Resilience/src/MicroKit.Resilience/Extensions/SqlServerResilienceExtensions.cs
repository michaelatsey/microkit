using MicroKit.Resilience.Builder;
using MicroKit.Resilience.Data.SqlServer;
using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Resilience.Extensions;

public static class SqlServerResilienceExtensions
{
    public static MicroKitResilienceBuilder AddSqlServer(this MicroKitResilienceBuilder builder)
    {
        // On ajoute le détecteur spécifique au builder
        builder.AddDetector<SqlResilienceDetector>();
        return builder;
    }
}
