using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// Adds <see cref="AggregateVersionConvention"/>, <see cref="AggregateBoundaryConvention"/>,
/// <see cref="AggregateReferenceConvention"/>, <see cref="AggregateErrorNameConvention"/> and
/// <see cref="SoftDeleteConvention"/> to the context's conventions.
/// </summary>
internal sealed class AggregateConventionSetPlugin : IConventionSetPlugin
{
    /// <inheritdoc/>
    public ConventionSet ModifyConventions(ConventionSet conventionSet)
    {
        ArgumentNullException.ThrowIfNull(conventionSet);
        // First: EF Core's table-sharing concurrency convention must see the version token.
        conventionSet.ModelFinalizingConventions.Insert(0, new AggregateVersionConvention());
        // Second: the deletion mark it adds goes through the model's naming conventions too.
        conventionSet.ModelFinalizingConventions.Insert(1, new SoftDeleteConvention());
        conventionSet.ModelFinalizingConventions.Add(new AggregateBoundaryConvention());
        conventionSet.ModelFinalizingConventions.Add(new AggregateReferenceConvention());
        conventionSet.ModelFinalizingConventions.Add(new AggregateErrorNameConvention());
        return conventionSet;
    }
}
