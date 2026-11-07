using MicroKit.Result;
using MicroKit.Result.AspNetCore;
using Microsoft.AspNetCore.Http;

namespace MicroKit.Result.Samples.Readme;

// README "Quick Start": its C# blocks are here verbatim, modulo indentation; the rest only makes them build. Keep them identical to README.md.
internal static class QuickStartSamples
{
    internal static void QuickStartSample(Guid id, IUserMapper mapper)
    {
        // Create
        Result<User> result = GetUser(id);

        // Transform
        Result<UserDto> dto = result.Map(u => mapper.ToDto(u));

        // Chain
        Result<Invoice> invoice = GetUser(id)
            .Bind(user => GetCart(user.CartId))
            .Bind(cart => CreateInvoice(cart));

        // Consume
        IResult response = invoice.ToHttpResult(); // 200 OK or ProblemDetails
    }

    internal sealed record User(Guid CartId);

    internal sealed record UserDto;

    internal sealed record Cart;

    internal sealed record Invoice;

    internal interface IUserMapper
    {
        UserDto ToDto(User user);
    }

    private static Result<User> GetUser(Guid id) => throw new NotSupportedException();

    private static Result<Cart> GetCart(Guid cartId) => throw new NotSupportedException();

    private static Result<Invoice> CreateInvoice(Cart cart) => throw new NotSupportedException();
}
