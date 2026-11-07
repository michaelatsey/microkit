using MicroKit.Result;
using MicroKit.Result.AspNetCore;

// ─── Demo runner (top-level statements must precede type declarations) ────────

Console.WriteLine("═══ MicroKit.Result Samples ═══\n");

// 1. Happy path
Console.WriteLine("── 1. Successful order ──");
var placed = OrderService.PlaceOrder(new(FakeDb.WidgetId, 2, "user@example.com", "4111111111111111"));
Console.WriteLine(placed.Match(
    onSuccess: o => $"  Confirmed: {o.Total:C}",
    onFailure: e => $"  Failed: {e.Message}"));

// 2. Product not found → HTTP 404
Console.WriteLine("\n── 2. Product not found → HTTP 404 ──");
var notFound = OrderService.PlaceOrder(new(Guid.NewGuid(), 1, "user@example.com", "4111111111111111"));
if (notFound.IsFailure)
    Console.WriteLine($"  HTTP {ResultProblemDetailsFactory.ToStatusCode(notFound.Error.Category)}");

// 3. Insufficient stock → HTTP 422
Console.WriteLine("\n── 3. Insufficient stock → HTTP 422 ──");
var outOfStock = OrderService.PlaceOrder(new(FakeDb.WidgetId, 50, "user@example.com", "4111111111111111"));
if (outOfStock.IsFailure)
    Console.WriteLine($"  HTTP {ResultProblemDetailsFactory.ToStatusCode(outOfStock.Error.Category)}: {outOfStock.Error.Message}");

// 4. Payment declined → HTTP 422
Console.WriteLine("\n── 4. Payment declined → HTTP 422 ──");
var declined = OrderService.PlaceOrder(new(FakeDb.WidgetId, 1, "user@example.com", "4000000000000002"));
if (declined.IsFailure)
    Console.WriteLine($"  HTTP {ResultProblemDetailsFactory.ToStatusCode(declined.Error.Category)}: {declined.Error.Message}");

// 5. Validation errors (multiple, collected)
Console.WriteLine("\n── 5. Validation errors (collected) ──");
var invalid = OrderService.PlaceOrder(new(Guid.NewGuid(), -1, "not-an-email", "0000"));
if (invalid.IsFailure && invalid.Error is ErrorCollection col)
{
    Console.WriteLine($"  {col.Count} errors:");
    foreach (var e in col)
        Console.WriteLine($"  • {e.Code}: {e.Message}");
}

// 6. Compensate — recover from failure with a fallback
Console.WriteLine("\n── 6. Compensate (fallback on failure) ──");
var product = FakeDb.FindProduct(Guid.NewGuid())
    .Compensate(_ => Result.Success(new Product(Guid.Empty, "Default", 0m, 0)))
    .Map(p => p.Name);
Console.WriteLine($"  Got: {product.GetValueOrDefault("N/A")}");

// 7. LINQ query syntax
Console.WriteLine("\n── 7. LINQ query syntax ──");
var priceWithTax =
    from p in FakeDb.FindProduct(FakeDb.WidgetId)
    from taxed in Result.Success(p.Price * 1.2m)
    select $"{p.Name}: {taxed:C} (inc. VAT)";
Console.WriteLine($"  {priceWithTax.GetValueOrDefault("unavailable")}");

// 8. ErrorCode predefined constants + comparison
Console.WriteLine("\n── 8. ErrorCode predefined + comparison ──");
Console.WriteLine($"  ErrorCode.NotFound  = {ErrorCode.NotFound}");
Console.WriteLine($"  ErrorCode.Timeout   = {ErrorCode.Timeout}");
Console.WriteLine($"  Conflict < NotFound : {ErrorCode.Conflict < ErrorCode.NotFound}");

// 9. ValidationError factories
Console.WriteLine("\n── 9. ValidationError factories ──");
Console.WriteLine($"  {ValidationError.Required("Name")}");
Console.WriteLine($"  {ValidationError.MinLength("Password", 8)}");
Console.WriteLine($"  {ValidationError.OutOfRange("Age", min: 0, max: 120)}");
Console.WriteLine($"  {ValidationError.InvalidEmail()}");

// 10. ErrorMetadataBuilder
Console.WriteLine("\n── 10. ErrorMetadataBuilder ──");
var meta = new ErrorMetadataBuilder()
    .WithTimestamp()
    .WithCorrelationId("req-abc-123")
    .Add("userId", "user-42")
    .Build();
foreach (var (k, v) in meta)
    Console.WriteLine($"  {k}: {v}");

// 11. ErrorCollection filter + group
Console.WriteLine("\n── 11. ErrorCollection filter + group ──");
var errors = ErrorCollection.From(
    ValidationError.Required("Email"),
    ValidationError.MinLength("Password", 8),
    new ProductNotFoundError(Guid.NewGuid()));

Console.WriteLine($"  Validation only: {errors.WithCategory(ErrorCategory.Validation).Count}");
foreach (var (cat, group) in errors.GroupByCategory())
    Console.WriteLine($"  {cat}: {group.Count} error(s)");

// 12. TryGetValue + Finally
Console.WriteLine("\n── 12. TryGetValue + Finally ──");
Result<string> maybe = Result.Success("hello");
var observed = maybe.Finally(r => Console.WriteLine($"  Finally — IsSuccess: {r.IsSuccess}"));
if (observed.TryGetValue(out var val))
    Console.WriteLine($"  Got: {val}");

Console.WriteLine("\n═══ Done ═══");

// ─── Domain errors ────────────────────────────────────────────────────────────

sealed record ProductNotFoundError(Guid ProductId)
    : Error(ErrorCode.From("PRODUCT.NOT_FOUND"), $"Product {ProductId} not found")
{
    public override ErrorCategory Category => ErrorCategory.NotFound;
}

sealed record InsufficientStockError(Guid ProductId, int Requested, int Available)
    : Error(ErrorCode.From("PRODUCT.INSUFFICIENT_STOCK"),
        $"Product {ProductId}: requested {Requested}, only {Available} in stock")
{
    public override ErrorCategory Category => ErrorCategory.BusinessRule;
}

sealed record PaymentDeclinedError(string Reason)
    : Error(ErrorCode.From("PAYMENT.DECLINED"), $"Payment declined: {Reason}")
{
    // The issuer answered and refused: a business outcome (422), not a failed dependency (502)
    public override ErrorCategory Category => ErrorCategory.BusinessRule;
}

// ─── Domain models ────────────────────────────────────────────────────────────

sealed record Product(Guid Id, string Name, decimal Price, int Stock);
sealed record Order(Guid Id, Product Product, int Quantity, decimal Total);
sealed record PlaceOrderRequest(Guid ProductId, int Quantity, string? Email, string CardNumber);

// ─── Fakes ────────────────────────────────────────────────────────────────────

static class FakeDb
{
    public static readonly Guid WidgetId = Guid.Parse("11111111-0000-0000-0000-000000000000");
    static readonly Product Widget = new(WidgetId, "Widget Pro", 29.99m, 10);

    public static Result<Product> FindProduct(Guid id) =>
        id == Widget.Id
            ? Result.Success(Widget)
            : Result.Failure<Product>(new ProductNotFoundError(id));
}

static class FakePayment
{
    public static Result Charge(string cardNumber, decimal amount)
    {
        if (cardNumber == "4111111111111111")
            return Result.Success();
        return Result.Failure(new PaymentDeclinedError("Card declined by issuer"));
    }
}

// ─── Application service ──────────────────────────────────────────────────────

static class OrderService
{
    public static Result<Order> PlaceOrder(PlaceOrderRequest req)
    {
        var validation = new ValidationResult()
            .AddErrorIf(req.Quantity <= 0, nameof(req.Quantity), "Quantity must be greater than zero.")
            .AddErrorIf(req.Quantity > 100, nameof(req.Quantity), "Cannot order more than 100 units.")
            .AddErrorIf(string.IsNullOrWhiteSpace(req.Email), nameof(req.Email), "Email is required.")
            .AddErrorIf(!IsValidEmail(req.Email), nameof(req.Email), "Email is not a valid address.");

        if (!validation.IsValid)
            return Result.Failure<Order>(validation.ToResult().Error);

        // Bind, not Ensure: the error reads p.Stock, which exists only once the product is found
        return FakeDb.FindProduct(req.ProductId)
            .Bind(p => p.Stock >= req.Quantity
                ? Result.Success(p)
                : Result.Failure<Product>(new InsufficientStockError(req.ProductId, req.Quantity, p.Stock)))
            .Bind(product => FakePayment.Charge(req.CardNumber, product.Price * req.Quantity)
                .Match(
                    onSuccess: () => Result.Success(
                        new Order(Guid.NewGuid(), product, req.Quantity, product.Price * req.Quantity)),
                    onFailure: e => Result<Order>.Failure(e)))
            .TapError(e => Console.WriteLine($"  [ERROR] {e.Code}: {e.Message}"))
            .Tap(o => Console.WriteLine($"  [OK] Order {o.Id} — {o.Product.Name} x{o.Quantity} = {o.Total:C}"));
    }

    static bool IsValidEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email) && email.Contains('@') && email.Contains('.');
}
