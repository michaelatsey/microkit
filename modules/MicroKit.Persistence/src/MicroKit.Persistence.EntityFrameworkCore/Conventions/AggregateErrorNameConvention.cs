using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// Resolves the error name of every top-most aggregate root — the <c>&lt;ROOT&gt;</c> segment of the
/// <see cref="PersistenceErrors"/> codes — and stores it on the root as the <c>MicroKit:ErrorName</c>
/// annotation, which the runtime model keeps.
/// </summary>
/// <remarks>
/// Model building fails when two roots resolve to the same name, when the annotation is on a type that
/// is not a top-most root, and when a root's default name (<see cref="PersistenceErrors.DefaultErrorName"/>)
/// is not a valid error name.
/// </remarks>
internal sealed class AggregateErrorNameConvention : IModelFinalizingConvention
{
    /// <summary>The model annotation that holds a root's error name.</summary>
    internal const string ErrorNameAnnotation = "MicroKit:ErrorName";

    /// <inheritdoc/>
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        var roots = new Dictionary<string, IConventionEntityType>(StringComparer.Ordinal);
        foreach (var type in modelBuilder.Metadata.GetEntityTypes())
        {
            var configured = type.FindAnnotation(ErrorNameAnnotation)?.Value as string;
            if (!AggregateBoundaryConvention.IsTopMostRoot(type))
            {
                if (configured is not null)
                {
                    throw new InvalidOperationException(
                        $"'{type.DisplayName()}' has the error name '{configured}' (HasAggregateErrorName), but it is not " +
                        "the top-most aggregate root of its hierarchy: only a root names the codes of its aggregate. " +
                        "Set it on the top-most root.");
                }

                continue;
            }

            var name = configured ?? PersistenceErrors.DefaultErrorName(type.ClrType);
            if (!PersistenceErrors.IsValidErrorName(name))
            {
                throw new InvalidOperationException(
                    $"The aggregate root '{type.DisplayName()}' has no valid default error name ('{name}'): the codes " +
                    "PERSISTENCE.<ROOT>.<REASON> need UPPER_SNAKE (A–Z, 0–9, single underscores). Give it one with " +
                    "HasAggregateErrorName(\"...\").");
            }

            if (roots.TryGetValue(name, out var other))
            {
                throw new InvalidOperationException(
                    $"The aggregate roots '{other.DisplayName()}' and '{type.DisplayName()}' both resolve to the error " +
                    $"name '{name}': their codes PERSISTENCE.{name}.<REASON> could not be told apart. Give one of them " +
                    "another name with HasAggregateErrorName(\"...\").");
            }

            roots[name] = type;
            type.Builder.HasAnnotation(ErrorNameAnnotation, name);
        }
    }

    /// <summary>Gets the error name of a top-most root.</summary>
    internal static string? ErrorNameOf(IReadOnlyEntityType root) => root.FindAnnotation(ErrorNameAnnotation)?.Value as string;
}
