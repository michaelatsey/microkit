using System.Linq.Expressions;
using MicroKit.Domain.Specifications;
using Shouldly;
using Xunit;

namespace MicroKit.Domain.UnitTests.Specifications;

public sealed class SpecificationTests
{
    private static readonly Candidate[] Candidates =
    [
        new(Age: 17, Active: false),
        new(Age: 17, Active: true),
        new(Age: 30, Active: false),
        new(Age: 30, Active: true),
    ];

    [Theory]
    [InlineData(17, false)]
    [InlineData(18, true)]
    [InlineData(30, true)]
    public void IsSatisfiedBy_SpecDefiningOnlyToExpression_EvaluatesTheExpression(int age, bool expected)
    {
        new AdultSpec().IsSatisfiedBy(new Candidate(age, Active: false)).ShouldBe(expected);
    }

    public static TheoryData<string> Composites => ["and", "or", "not", "nested"];

    [Theory]
    [MemberData(nameof(Composites))]
    public void IsSatisfiedBy_Composite_AgreesWithItsExpression(string composite)
    {
        var spec = Build(composite);
        var expression = spec.ToExpression().Compile();

        var results = Candidates.Select(c => spec.IsSatisfiedBy(c)).ToArray();

        results.ShouldBe(Candidates.Select(expression).ToArray());
        results.ShouldContain(true);
        results.ShouldContain(false);
    }

    [Fact]
    public void IsSatisfiedBy_Composites_MatchTheirBooleanDefinition()
    {
        var adult = new AdultSpec();
        var active = new ActiveSpec();

        foreach (var c in Candidates)
        {
            var isAdult = c.Age >= 18;
            adult.And(active).IsSatisfiedBy(c).ShouldBe(isAdult && c.Active);
            adult.Or(active).IsSatisfiedBy(c).ShouldBe(isAdult || c.Active);
            adult.Not().IsSatisfiedBy(c).ShouldBe(!isAdult);
        }
    }

    [Fact]
    public void IsSatisfiedBy_CompilesTheExpressionOncePerInstance()
    {
        var spec = new CountingSpec();

        spec.IsSatisfiedBy(new Candidate(30, Active: true)).ShouldBeTrue();
        spec.IsSatisfiedBy(new Candidate(17, Active: true)).ShouldBeFalse();
        spec.IsSatisfiedBy(new Candidate(40, Active: true)).ShouldBeTrue();

        spec.ToExpressionCalls.ShouldBe(1);
    }

    [Fact]
    public void IsSatisfiedBy_CacheIsPerInstance()
    {
        var first = new CountingSpec();
        var second = new CountingSpec();

        first.IsSatisfiedBy(new Candidate(30, Active: true));
        second.IsSatisfiedBy(new Candidate(30, Active: true));

        first.ToExpressionCalls.ShouldBe(1);
        second.ToExpressionCalls.ShouldBe(1);
    }

    [Fact]
    public void IsSatisfiedBy_ConcurrentFirstUse_NeverYieldsAWrongResult()
    {
        var spec = new AdultSpec();

        var results = Enumerable.Range(0, 64)
            .AsParallel()
            .Select(i => (Expected: i % 2 == 0, Actual: spec.IsSatisfiedBy(new Candidate(i % 2 == 0 ? 30 : 10, false))))
            .ToArray();

        results.ShouldAllBe(r => r.Expected == r.Actual);
    }

    [Fact]
    public void IsSatisfiedBy_IsNotOverridable()
    {
        var method = typeof(Specification<Candidate>).GetMethod(nameof(Specification<Candidate>.IsSatisfiedBy))!;

        (method.IsVirtual && !method.IsFinal).ShouldBeFalse();
        typeof(Specification<Candidate>).GetMethod(nameof(Specification<Candidate>.ToExpression))!
            .IsAbstract.ShouldBeTrue();
    }

    private static ISpecification<Candidate> Build(string composite) => composite switch
    {
        "and" => new AdultSpec().And(new ActiveSpec()),
        "or" => new AdultSpec().Or(new ActiveSpec()),
        "not" => new AdultSpec().Not(),
        "nested" => ((Specification<Candidate>)new AdultSpec().Not()).Or(new AdultSpec().And(new ActiveSpec())),
        _ => throw new ArgumentOutOfRangeException(nameof(composite)),
    };

    public sealed record Candidate(int Age, bool Active);

    private sealed class AdultSpec : Specification<Candidate>
    {
        public override Expression<Func<Candidate, bool>> ToExpression() => c => c.Age >= 18;
    }

    private sealed class ActiveSpec : Specification<Candidate>
    {
        public override Expression<Func<Candidate, bool>> ToExpression() => c => c.Active;
    }

    private sealed class CountingSpec : Specification<Candidate>
    {
        public int ToExpressionCalls { get; private set; }

        public override Expression<Func<Candidate, bool>> ToExpression()
        {
            ToExpressionCalls++;
            return c => c.Age >= 18;
        }
    }
}
