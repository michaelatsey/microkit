using Shouldly;

namespace MicroKit.Domain.ArchitectureTests;

// The module's guides, performance notes, .claude/ files and README must not teach the surface
// #160 replaced (#163). The project file copies them to module-docs/ in the output.
public sealed class DocumentationExamplesTests
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "module-docs");

    private static readonly string[] ReplacedSurface =
    [
        ": DomainEvent;",
        "DateTimeOffset.UtcNow",
        "Guid.NewGuid",
        "FluentAssertions",
        "=> new(Guid.Empty)",
        "IReadOnlyCollection<IDomainEvent>",
        "Touch(",
    ];

    // Lines that name a replaced or forbidden API in order to forbid it. Each entry is a
    // module-relative path and a fragment of the allowed line.
    private static readonly (string File, string Line)[] Prohibitions =
    [
        (".claude/rules/testing.md", "**FluentAssertions est interdit**"),
        (".claude/agents/release-manager.md", "No `FluentAssertions` anywhere in the package graph"),
        (".claude/agents/dependency-guardian.md", "no FluentAssertions."),
        (".claude/agents/dependency-guardian.md", "`FluentAssertions` is **banned** everywhere"),
        (".claude/agents/dependency-guardian.md", "# Check for banned FluentAssertions anywhere"),
        (".claude/agents/dependency-guardian.md", "grep -rn 'FluentAssertions' modules/MicroKit.Domain/"),
        (".claude/rules/ddd-patterns.md", "❌ Accès à DateTime.Now / DateTimeOffset.UtcNow directement"),
    ];

    [Fact]
    public void Documentation_IsPresent()
    {
        File.Exists(Path.Combine(Root, "README.md")).ShouldBeTrue();
        File.Exists(Path.Combine(Root, "docs", "guides", "Implementing-Aggregates.md")).ShouldBeTrue();
        File.Exists(Path.Combine(Root, "docs", "performance", "ValueObject-Benchmarks.md")).ShouldBeTrue();
        File.Exists(Path.Combine(Root, ".claude", "rules", "csharp-style.md")).ShouldBeTrue();
    }

    [Fact]
    public void PurityRule_SaysABuiltInActorNeedsTheTrustedConverter()
    {
        // #177: the rule must not claim that every Actor round-trips with default options.
        var rule = File.ReadAllText(Path.Combine(Root, ".claude", "rules", "domain-purity.md"));

        rule.ShouldContain("`Money`, `Currency` et un `Actor` utilisateur font l'aller-retour avec les options par défaut.");
        rule.ShouldContain("**ne** le fait **pas**");
        rule.ShouldContain("le refuse à l'écriture (#175) et à la lecture (#170)");
        rule.ShouldContain("enregistrer `TrustedActorJsonConverter`");
        rule.ShouldNotContain("un `Money` ou un `Actor` passe par l'outbox");
    }

    [Fact]
    public void Guide_DeclaresTheRuleItsAuditableExampleChecks()
    {
        var guide = File.ReadAllText(Path.Combine(Root, "docs", "guides", "Implementing-Aggregates.md"));

        guide.ShouldContain("CheckRule(new InvoiceTotalMustBePositiveRule(total));");
        guide.ShouldContain("public sealed class InvoiceTotalMustBePositiveRule(Money total) : BusinessRule");
    }

    [Fact]
    public void Adr003_DescribesTheShippedEventRollback()
    {
        var adr = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "module-adr", "ADR-003-Domain-Surface-Before-Stable.md"));

        adr.ShouldNotContain("truncates its event list to the count");
        adr.ShouldNotContain("(default, offset, monotonicity)");
        adr.ShouldContain("any offset is accepted");
        adr.ShouldContain("`IEventJournal`");
        adr.ShouldContain("across a drain inside");
        adr.ShouldContain("`AuditRules.Mutate`");
    }

    [Theory]
    [InlineData("AuditableEntity")]
    [InlineData("AuditableAggregateRoot")]
    public void AuditableBase_DeclaresItsPrivateFieldsBeforeItsConstructors(string type)
    {
        // .claude/rules/csharp-style.md, member ordering: private backing fields, then constructors.
        var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "module-src", type + ".cs"));

        var lastField = Array.FindLastIndex(lines, l => l.StartsWith("    private ", StringComparison.Ordinal) && l.TrimEnd().EndsWith(';'));
        var firstConstructor = Array.FindIndex(lines, l => l.StartsWith($"    protected {type}(", StringComparison.Ordinal));

        lastField.ShouldBeGreaterThan(-1);
        firstConstructor.ShouldBeGreaterThan(lastField);
    }

    [Fact]
    public void Documentation_TeachesNoReplacedSurface()
    {
        var offenders =
            from path in Directory.EnumerateFiles(Root, "*.md", SearchOption.AllDirectories)
            let file = Path.GetRelativePath(Root, path).Replace('\\', '/')
            from line in File.ReadLines(path).Select((text, index) => (Text: text, Number: index + 1))
            where ReplacedSurface.Any(pattern => line.Text.Contains(pattern, StringComparison.Ordinal))
            where !Prohibitions.Any(p => p.File == file && line.Text.Contains(p.Line, StringComparison.Ordinal))
            select $"{file}:{line.Number}: {line.Text.Trim()}";

        offenders.ShouldBeEmpty();
    }
}
