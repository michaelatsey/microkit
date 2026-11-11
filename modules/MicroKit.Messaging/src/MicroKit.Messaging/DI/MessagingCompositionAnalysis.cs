namespace MicroKit.Messaging;

using System.Text;

/// <summary>
/// The contract rules of a composition, in one place: pure, with no hosting dependency.
/// <c>IntegrationEventRegistry</c> and <c>MessagingCompositionValidator</c> both call it, so a
/// container started by a host and one resolved by hand reject the same compositions with the same
/// messages.
/// </summary>
/// <remarks>
/// <para>The rules, each reported with its fix:</para>
/// <list type="bullet">
/// <item><description>
/// A declared type whose <see cref="MessageContractAttribute"/> cannot be read (its constructor
/// rejects a blank name or a name carrying a version suffix).
/// </description></item>
/// <item><description>A published type without <see cref="MessageContractAttribute"/>.</description></item>
/// <item><description>A handled type without <see cref="MessageContractAttribute"/>.</description></item>
/// <item><description>
/// Two types claiming one wire type (<c>&lt;name&gt;.v&lt;version&gt;</c>), whether they publish
/// it, handle it, or one of each. Two versions of one name on two types are distinct contracts.
/// </description></item>
/// <item><description>
/// Two <c>AddMicroKitMessaging</c> calls setting different <c>bus.ServiceName</c> values.
/// </description></item>
/// </list>
/// <para>
/// <b>One CLR type maps to exactly one contract</b>, structurally: the attribute allows one usage
/// per type and is not inherited, so there is nothing for a rule to check.
/// </para>
/// </remarks>
internal static class MessagingCompositionAnalysis
{
    /// <summary>Every contract problem of <paramref name="composition"/>, in a stable order.</summary>
    /// <param name="composition">The composition to analyse.</param>
    /// <returns>One message per problem; empty when the composition is valid.</returns>
    public static IReadOnlyList<string> Analyze(MessagingComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);
        var problems = new List<string>();

        // Every declared type once — published, handled, or both — in declaration order. Each rule
        // below reads this one set, so a new kind of declaration is added in one place.
        var declared = composition.Publications
            .Concat(composition.Handled.Select(h => h.MessageType))
            .Distinct()
            .ToList();
        var published = composition.Publications.ToHashSet();

        foreach (var type in declared)
        {
            if (composition.UnreadableContractOf(type) is { } error)
            {
                problems.Add(
                    $"'{type.FullName}' carries a [MessageContract] that cannot be read: {error} Fix " +
                    "the attribute's arguments.");
                continue;
            }

            if (composition.ContractOf(type) is not null)
            {
                continue;
            }

            if (published.Contains(type))
            {
                problems.Add(
                    $"'{type.FullName}' is published (bus.Publishes<{type.Name}>()) but has no " +
                    "[MessageContract]. Add [MessageContract(\"<name>\")] to the type: the wire name " +
                    "cannot be the CLR type name, because a consumer in another service holds another type.");
            }
            else
            {
                var handlers = string.Join(", ", composition.Handled
                    .Where(h => h.MessageType == type)
                    .Select(h => $"'{h.HandlerType.FullName}'"));
                problems.Add(
                    $"'{type.FullName}' is handled by {handlers} but has no [MessageContract]. Add " +
                    "[MessageContract(\"<name>\")] to the type, with the name its producer publishes " +
                    "under: nothing else binds a message arriving on the wire to this local type.");
            }
        }

        var claimants = declared
            .Select(t => (Type: t, Contract: composition.ContractOf(t)))
            .Where(c => c.Contract is not null)
            .GroupBy(c => c.Contract!.WireType, StringComparer.Ordinal);

        foreach (var group in claimants.Where(g => g.Count() > 1))
        {
            var types = string.Join(" and ", group.Select(c => $"'{c.Type.FullName}'"));
            problems.Add(
                $"Wire type '{group.Key}' is claimed by {types}. A wire type resolves to exactly one " +
                "local type: a message is deserialized once, before any handler runs, so two " +
                "claimants could only be served by picking one and producing the wrong shape. Share " +
                "one type, or give one of them another name or Version.");
        }

        var names = composition.ServiceNames;
        if (names.Count > 1)
        {
            problems.Add(
                $"bus.ServiceName is set to {string.Join(" and ", names.Select(n => $"'{n}'"))} by " +
                "different AddMicroKitMessaging calls. A service has one name, which is its endpoint " +
                "and the source of what it publishes: set it in one call, or in configuration " +
                $"('{MessagingOptions.SectionName}:ServiceName').");
        }

        return problems;
    }

    /// <summary>Throws one exception listing every problem, when there is any.</summary>
    /// <param name="problems">The problems found.</param>
    /// <exception cref="IntegrationEventConfigurationException">
    /// <paramref name="problems"/> is not empty.
    /// </exception>
    public static void ThrowIfAny(IReadOnlyList<string> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);
        if (problems.Count == 0)
        {
            return;
        }

        var message = new StringBuilder()
            .Append("The MicroKit.Messaging composition is invalid (")
            .Append(problems.Count)
            .Append(problems.Count == 1 ? " problem):" : " problems):");

        for (var i = 0; i < problems.Count; i++)
        {
            message.AppendLine().Append(i + 1).Append(". ").Append(problems[i]);
        }

        throw new IntegrationEventConfigurationException(message.ToString());
    }
}
