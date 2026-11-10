using Microsoft.EntityFrameworkCore.Metadata;

namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// The tables and columns a reserved property is checked against, when the model is finalised: the column
/// collision checks of <see cref="AggregateVersionConvention"/> and <see cref="SoftDeleteConvention"/>.
/// </summary>
internal static class TableColumns
{
    /// <summary>
    /// The tables that store the properties of <paramref name="hierarchy"/>: its own table (TPH), each type's
    /// (TPT), or each concrete type's (TPC, whose abstract base has no table).
    /// </summary>
    internal static IReadOnlyList<StoreObjectIdentifier> TablesOf(IReadOnlyEntityType hierarchy) =>
        [.. hierarchy.GetDerivedTypesInclusive()
            .Select(type => StoreObjectIdentifier.Create(type, StoreObjectType.Table))
            .OfType<StoreObjectIdentifier>()
            .Distinct()];

    /// <summary>
    /// Every property stored in <paramref name="table"/>, with its column: the properties of the entity types
    /// mapped to it, whole or by a fragment. <c>GetColumnName(table)</c> alone is not enough: it names a column
    /// for a TPC property in any table.
    /// </summary>
    internal static IEnumerable<(IConventionProperty Property, string Column)> PropertiesIn(
        IConventionModel model,
        StoreObjectIdentifier table)
    {
        foreach (var type in model.GetEntityTypes())
        {
            if (StoreObjectIdentifier.Create(type, StoreObjectType.Table) != table
                && !type.GetMappingFragments(StoreObjectType.Table).Any(fragment => fragment.StoreObject == table))
            {
                continue;
            }

            foreach (var property in type.GetProperties())
            {
                if (property.GetColumnName(table) is { } column)
                    yield return (property, column);
            }
        }
    }
}
