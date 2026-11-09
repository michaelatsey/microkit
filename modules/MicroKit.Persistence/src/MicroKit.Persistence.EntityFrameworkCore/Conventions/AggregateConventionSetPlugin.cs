using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// Adds <see cref="AggregateVersionConvention"/>, <see cref="AggregateBoundaryConvention"/> and
/// <see cref="AggregateReferenceConvention"/> to the context's conventions.
/// </summary>
internal sealed class AggregateConventionSetPlugin : IConventionSetPlugin
{
    /// <inheritdoc/>
    public ConventionSet ModifyConventions(ConventionSet conventionSet)
    {
        ArgumentNullException.ThrowIfNull(conventionSet);
        // First: EF Core's table-sharing concurrency convention must see the version token.
        conventionSet.ModelFinalizingConventions.Insert(0, new AggregateVersionConvention());
        conventionSet.ModelFinalizingConventions.Add(new AggregateBoundaryConvention());
        conventionSet.ModelFinalizingConventions.Add(new AggregateReferenceConvention());
        return conventionSet;
    }
}
