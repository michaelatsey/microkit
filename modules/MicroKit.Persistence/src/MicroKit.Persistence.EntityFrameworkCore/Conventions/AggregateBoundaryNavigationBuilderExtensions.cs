using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// Model-building extensions that shape an aggregate's boundary.
/// </summary>
public static class AggregateBoundaryNavigationBuilderExtensions
{
    /// <summary>
    /// Excludes the navigation from its aggregate's boundary: its target is reference data, never
    /// loaded with the aggregate, and not counted as part of it.
    /// </summary>
    /// <param name="navigationBuilder">The builder of the navigation, from <c>Navigation(...)</c>.</param>
    /// <returns><paramref name="navigationBuilder"/> for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// By default every navigation to an entity that is not an <see cref="IAggregateRoot"/> is
    /// inside the boundary, recursively, and an entity reached from two roots fails model building.
    /// Mark the navigations to shared reference data (a country, a currency) with this method:
    /// <code>
    /// modelBuilder.Entity&lt;Order&gt;().Navigation(o =&gt; o.Country).IsOutsideAggregate();
    /// </code>
    /// </para>
    /// <para>
    /// It applies to reference, collection and skip (many-to-many) navigations. It sets the model
    /// annotation <c>MicroKit:OutsideAggregate</c> to <see langword="true"/>.
    /// </para>
    /// </remarks>
    public static NavigationBuilder IsOutsideAggregate(this NavigationBuilder navigationBuilder)
    {
        ArgumentNullException.ThrowIfNull(navigationBuilder);
        navigationBuilder.HasAnnotation(AggregateIncludePlan.OutsideAggregateAnnotation, true);
        return navigationBuilder;
    }
}
