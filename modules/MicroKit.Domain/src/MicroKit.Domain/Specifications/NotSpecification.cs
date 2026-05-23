namespace MicroKit.Domain.Specifications;

internal sealed class NotSpecification<T> : Specification<T>
{
    private readonly ISpecification<T> _specification;

    internal NotSpecification(ISpecification<T> specification)
    {
        _specification = specification;
    }

    public override bool IsSatisfiedBy(T candidate)
    {
        return !_specification.IsSatisfiedBy(candidate);
    }

    public override Expression<Func<T, bool>> ToExpression()
    {
        var expression = _specification.ToExpression();
        var parameter = Expression.Parameter(typeof(T));
        var visitor = new ReplaceExpressionVisitor(expression.Parameters[0], parameter);
        var body = visitor.Visit(expression.Body)!;

        return Expression.Lambda<Func<T, bool>>(Expression.Not(body), parameter);
    }
}