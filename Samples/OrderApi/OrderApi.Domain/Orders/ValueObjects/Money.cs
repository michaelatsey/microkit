namespace OrderApi.Domain.Orders.ValueObjects;

public sealed record Money(decimal Amount, string Currency)
{
    public static readonly Money Zero = new(0m, "USD");

    public static Money Of(decimal amount, string currency = "USD")
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        return new Money(amount, currency);
    }

    public static Money Sum(IEnumerable<Money> items)
    {
        var list = items.ToList();
        if (list.Count == 0) return Zero;
        var currency = list[0].Currency;
        return new Money(list.Sum(m => m.Amount), currency);
    }

    public static Money operator *(Money money, int quantity) => new(money.Amount * quantity, money.Currency);
    public static Money operator +(Money a, Money b) => new(a.Amount + b.Amount, a.Currency);
}
