using MicroKit.Domain.Aggregates;
using MicroKit.Domain.Exceptions;
using MicroKit.Domain.Identifiers;
using MicroKit.Domain.Rules;
using Shouldly;
using Xunit;

namespace MicroKit.Domain.UnitTests.Rules;

public sealed class BusinessRuleTests
{
    [Fact]
    public void Exception_CarriesTheRuleCode()
    {
        var rule = new OrderMustHaveItemsRule(itemCount: 0);

        var ex = new BusinessRuleViolationException(rule);

        ex.Code.ShouldBe("order.must-have-items");
        ex.ViolatedRule.ShouldBeSameAs(rule);
        ex.Message.ShouldBe("Business rule 'order.must-have-items' was violated: An order must contain at least one item.");
    }

    [Fact]
    public void CheckRule_BrokenRule_ThrowsWithTheRuleCode()
    {
        var order = new TestOrder(GuidId.New());

        var ex = Should.Throw<BusinessRuleViolationException>(() => order.Place(itemCount: 0));

        ex.Code.ShouldBe("order.must-have-items");
    }

    [Fact]
    public void CheckRule_SatisfiedRule_DoesNotThrow()
    {
        var order = new TestOrder(GuidId.New());

        Should.NotThrow(() => order.Place(itemCount: 1));
    }

    [Fact]
    public void Exception_NullRule_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => new BusinessRuleViolationException(null!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Exception_RuleWithoutCode_ThrowsArgumentException(string? code)
    {
        Should.Throw<ArgumentException>(() => new BusinessRuleViolationException(new CodelessRule(code!)));
    }

    private sealed class CodelessRule(string code) : BusinessRule
    {
        public override bool IsBroken() => true;

        public override string Message => "Always broken.";

        public override string Code => code;
    }

    private sealed class OrderMustHaveItemsRule(int itemCount) : BusinessRule
    {
        public override bool IsBroken() => itemCount == 0;

        public override string Message => "An order must contain at least one item.";

        public override string Code => "order.must-have-items";
    }

    private sealed class TestOrder(GuidId id) : AggregateRoot<GuidId>(id)
    {
        public void Place(int itemCount) => CheckRule(new OrderMustHaveItemsRule(itemCount));
    }
}
