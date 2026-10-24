using System.Text.RegularExpressions;
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
    public void ModuleBrain_NamesEverySourceFileAndTheNotFoundExceptionSignature()
    {
        var brain = File.ReadAllText(Path.Combine(Root, ".claude", "CLAUDE.md"));
        var sources = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "module-src", "files.txt"));

        // A whole file name: "Entity.cs" must not be satisfied by "AuditableEntity.cs".
        sources.ShouldNotBeEmpty();
        sources.Where(name => !Regex.IsMatch(brain, $@"(?<![\w.]){Regex.Escape(name)}\.cs\b")).ShouldBeEmpty();
        brain.ShouldContain("EntityNotFoundException<TEntity, TId>(TId entityId)");
    }

    [Fact]
    public void PurityRule_ListsEveryNamespaceTheSourcesImport()
    {
        var rule = File.ReadAllText(Path.Combine(Root, ".claude", "rules", "domain-purity.md"));
        var usings = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "module-src", "usings.txt"))
            .Select(line => line.Replace("global ", string.Empty, StringComparison.Ordinal))
            .Distinct()
            .ToArray();

        usings.ShouldContain("using System.Globalization;");
        usings.Where(directive => !rule.Contains(directive, StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public void PurityRule_SaysABuiltInActorNeedsTheTrustedConverter()
    {
        // #177: the rule must not claim that every Actor round-trips with default options.
        var rule = File.ReadAllText(Path.Combine(Root, ".claude", "rules", "domain-purity.md"));

        // Identifiers, not wording: the rule names the trusted converter, the read refusal and the
        // asymmetric decision that replaced the write refusal of #175 (#181), and no longer carries
        // the claim that any Actor crosses the outbox with default options.
        rule.ShouldContain("TrustedActorJsonConverter");
        rule.ShouldContain("(#170)");
        rule.ShouldContain("ADR-004 D1");
        rule.ShouldNotContain("(#175)");
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
    public void Guide_StatesThatAnAggregateIsDiscardedAfterADomainMethodThrows()
    {
        // #182, ADR-004 D2 and D3.
        var guide = File.ReadAllText(Path.Combine(Root, "docs", "guides", "Implementing-Aggregates.md"));

        guide.ShouldContain("### When a domain method throws");
        guide.ShouldContain("the aggregate instance must be discarded, not saved");
        guide.ShouldContain("Discarding means the change set too, not only the reference");
        guide.ShouldContain("IUnitOfWork.DiscardChanges()");
        guide.ShouldContain("the events the method raised outside `Mutate`");
        guide.ShouldNotContain("catch (DomainException)");
        guide.ShouldContain("allocates a closure and a delegate on each call");
    }

    [Fact]
    public void Adr004_StatesWhatHappensToValuesPersistedBeforeTheStricterRules()
    {
        var adr = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "module-adr", "ADR-004-Domain-Surface-Amendments.md"));

        adr.ShouldContain("A value persisted before D5, D6 or D8");
        adr.ShouldContain("no longer rehydrates");
        adr.ShouldContain("rewrite those rows too");
    }

    [Fact]
    public void Adr004_GivesAnAccurateAccountOfCachesAnd180()
    {
        // CachingBehavior handles queries, not commands; and a cache reads back what it wrote.
        var adr = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "module-adr", "ADR-004-Domain-Surface-Amendments.md"));

        adr.ShouldNotContain("idempotency and caching behaviours threw after the command");
        adr.ShouldNotContain("a cache entry is written by the application and read, if ever");
        adr.ShouldContain("its caching behaviour failed every query");
        adr.ShouldContain("idempotency store and caching behaviour, now writes a response carrying a built-in actor and fails");
    }

    [Theory]
    [InlineData("ADR-001-Records-First-Value-Objects.md")]
    [InlineData("ADR-002-Domain-Events-Collection-Strategy.md")]
    public void SupersededAdr_PointsToEveryAdrRecordingTheCurrentSurface(string file)
    {
        var note = string.Join(' ', File.ReadLines(Path.Combine(AppContext.BaseDirectory, "module-adr", file)).Take(6));

        note.ShouldContain("ADR-003-Domain-Surface-Before-Stable.md");
        note.ShouldContain("ADR-004-Domain-Surface-Amendments.md");
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
