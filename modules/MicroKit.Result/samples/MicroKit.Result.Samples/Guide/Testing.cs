using System.Diagnostics.CodeAnalysis;

namespace MicroKit.Result.Samples.Guide;

// Backs docs/guides/usage.md, "Test code that returns results": its C# block is here verbatim, modulo indentation;
// the rest only makes it build. Keep the two identical. The samples reference no test library, so Check stands for
// the assertion of yours.
internal static class TestingSamples
{
    internal static class OrderServiceTests
    {
        public static void Place_UnknownProduct_FailsWithNotFound()
        {
            Result<Order> result = OrderService.Place(UnknownProductId, quantity: 1);

            Check(result.IsFailure, "the result is a failure");             // the rail first
            Check(result.Error is ProductNotFoundError, "a typed error");   // then the error
            Check(result.Error.Category == ErrorCategory.NotFound, "a NotFound category");
        }

        public static void Place_KnownProduct_SucceedsWithTheQuantity()
        {
            Result<Order> result = OrderService.Place(KnownProductId, quantity: 2);

            Check(result.TryGetValue(out Order? order), "the result is a success");
            Check(order.Quantity == 2, "the order keeps the quantity");
        }

        public static void Default_IsNeitherSuccessNorFailure()
        {
            Result<Order> uninitialized = default;

            Check(!uninitialized.IsSuccess && !uninitialized.IsFailure, "neither rail");
        }

        // Your framework's assertion: Assert.True, ShouldBeTrue…
        private static void Check([DoesNotReturnIf(false)] bool condition, string expectation)
        {
            if (!condition)
                throw new InvalidOperationException($"Expected {expectation}.");
        }
    }

    internal static readonly Guid KnownProductId = Guid.Parse("22222222-0000-0000-0000-000000000000");

    internal static readonly Guid UnknownProductId = Guid.Parse("33333333-0000-0000-0000-000000000000");

    internal sealed record Order(Guid ProductId, int Quantity);

    internal sealed record ProductNotFoundError(Guid ProductId)
        : Error(ErrorCode.From("PRODUCT.NOT_FOUND"), $"Product {ProductId} not found")
    {
        public override ErrorCategory Category => ErrorCategory.NotFound;
    }

    internal static class OrderService
    {
        public static Result<Order> Place(Guid productId, int quantity) =>
            productId == KnownProductId
                ? Result.Success(new Order(productId, quantity))
                : new ProductNotFoundError(productId);
    }
}
