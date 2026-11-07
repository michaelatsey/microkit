namespace MicroKit.Result.Samples.Readme;

// README "LINQ Query Syntax": its C# blocks are here verbatim, modulo indentation; the rest only makes them build. Keep them identical to README.md.
internal static class LinqQuerySyntaxSamples
{
    internal static void LinqQuerySyntaxSample(Guid id)
    {
        var result =
            from user in GetUser(id)
            from cart in GetCart(user.CartId)
            from order in CreateOrder(cart)
            select order.ToDto();

        // Where (requires an error on false)
        Result<int> positive = Result.Success(42)
            .Where(v => v > 0, new NegativeValueError());

        // AsEnumerable — 0 or 1 element
        foreach (var value in result.AsEnumerable())
            Process(value);
    }

    internal sealed record User(Guid CartId);

    internal sealed record Cart;

    internal sealed record OrderDto(Guid Id);

    internal sealed record Order(Guid Id)
    {
        public OrderDto ToDto() => new(Id);
    }

    internal sealed record NegativeValueError() : Error(ErrorCode.From("VALUE.NEGATIVE"), "Value must be positive");

    private static Result<User> GetUser(Guid id) => throw new NotSupportedException();

    private static Result<Cart> GetCart(Guid cartId) => throw new NotSupportedException();

    private static Result<Order> CreateOrder(Cart cart) => throw new NotSupportedException();

    private static void Process(OrderDto dto) { }
}
