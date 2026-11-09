using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>Adds <see cref="AggregateBoundaryConvention"/> to the context's conventions.</summary>
internal sealed class AggregateConventionSetPlugin : IConventionSetPlugin
{
    /// <inheritdoc/>
    public ConventionSet ModifyConventions(ConventionSet conventionSet)
    {
        ArgumentNullException.ThrowIfNull(conventionSet);
        conventionSet.ModelFinalizingConventions.Add(new AggregateBoundaryConvention());
        return conventionSet;
    }
}
