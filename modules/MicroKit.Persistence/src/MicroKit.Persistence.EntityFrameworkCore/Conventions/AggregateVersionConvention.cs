using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// Adds the shadow <see cref="AggregateVersion.PropertyName"/> concurrency token to every aggregate
/// root, when the model is finalised. Added by <c>UseMicroKitPersistence()</c>, first among the
/// finalizing conventions, so EF Core's own table-sharing concurrency convention sees the token.
/// </summary>
/// <remarks>
/// <para>
/// The token goes on the top-most root of each hierarchy only: a derived root shares it. No column
/// name is set, so a naming convention or the application's <c>HasColumnName</c> applies.
/// </para>
/// <para>Model building fails, naming the root, when the reserved property:</para>
/// <list type="bullet">
///   <item><description>is mapped to a CLR member of the root or of a derived type;</description></item>
///   <item><description>is not a <see langword="long"/>;</description></item>
///   <item><description>is no longer a concurrency token;</description></item>
///   <item><description>is given a default value (or SQL), a computed column, value generation, a value converter or
///     a provider type: the save step alone writes the version, and the commit's <c>UPDATE</c> compares it;</description></item>
///   <item><description>shares its column with another property mapped to a table of the root's hierarchy — its
///     table, or each concrete type's under TPC.</description></item>
/// </list>
/// </remarks>
internal sealed class AggregateVersionConvention : IModelFinalizingConvention
{
    /// <inheritdoc/>
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        var model = modelBuilder.Metadata;

        foreach (var root in model.GetEntityTypes().Where(AggregateBoundaryConvention.IsTopMostRoot).ToList())
        {
            foreach (var type in root.GetDerivedTypesInclusive())
            {
                if (type.FindDeclaredProperty(AggregateVersion.PropertyName) is { } declared && !declared.IsShadowProperty())
                    throw Reserved(root, $"'{type.ClrType.Name}' maps it to a CLR member");
            }

            root.Builder.Property(typeof(long), AggregateVersion.PropertyName);
            var token = root.FindProperty(AggregateVersion.PropertyName);
            if (token is null || token.ClrType != typeof(long))
                throw Reserved(root, $"it is configured as '{token?.ClrType.Name}', not 'long'");

            token.Builder.IsConcurrencyToken(true);
            if (!token.IsConcurrencyToken)
                throw Reserved(root, "it is configured not to be a concurrency token");

            EnsureOnlyTheColumnIsConfigured(root, token);
            EnsureOwnColumn(model, root, token);
        }
    }

    // The save step writes the version (Added: 1, otherwise original + 1) and the UPDATE compares it: a value
    // the database or a converter supplies would be overwritten, or compared in another form.
    private static void EnsureOnlyTheColumnIsConfigured(IReadOnlyEntityType root, IConventionProperty token)
    {
        if (token.GetDefaultValueSql() is not null || token.TryGetDefaultValue(out _) || token.GetComputedColumnSql() is not null)
            throw Reserved(root, "it is given a default value, or a computed one");
        if (token.ValueGenerated != ValueGenerated.Never)
            throw Reserved(root, "it is configured as generated");
        if (token.GetValueConverter() is not null || token.GetProviderClrType() is not null)
            throw Reserved(root, "it is given a value converter");
    }

    // The token's column must not be shared with another property of a table that stores it: derived types
    // (TPH), owned types and other entity types split onto it; under TPC, each concrete type's table. A view or
    // SQL query root has no table to collide in.
    private static void EnsureOwnColumn(IConventionModel model, IConventionEntityType root, IConventionProperty token)
    {
        foreach (var table in TableColumns.TablesOf(root))
        {
            if (token.GetColumnName(table) is not { } column)
                continue;

            foreach (var (property, other) in TableColumns.PropertiesIn(model, table))
            {
                // Two roots under one mapped TPH base that is no root each have their own version, in one column.
                if (property.Name == AggregateVersion.PropertyName
                    && property.DeclaringType is IReadOnlyEntityType owner && AggregateBoundaryConvention.IsTopMostRoot(owner))
                {
                    continue;
                }

                if (!ReferenceEquals(property, token) && string.Equals(other, column, StringComparison.OrdinalIgnoreCase))
                {
                    throw Reserved(
                        root,
                        $"its column '{column}' in '{table.DisplayName()}' is also mapped by '{property.DeclaringType.ClrType.Name}.{property.Name}'");
                }
            }
        }
    }

    private static InvalidOperationException Reserved(IReadOnlyEntityType root, string reason) =>
        new($"'{AggregateVersion.PropertyName}' is reserved on the aggregate root '{root.ClrType.Name}' for its version, " +
            $"but {reason}. Rename that property or column; the version itself may only have its column configured.");
}
