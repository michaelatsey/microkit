namespace MicroKit.Result.Samples.Guide;

// Backs docs/guides/usage.md, "Catch mistakes with the analyzers": its C# blocks are here verbatim, modulo
// indentation; the rest only makes them build. Keep the two identical. The project consumes
// MicroKit.Result.Analyzers, so a Release build proves that none of these lines is reported.
internal static class AnalyzersSamples
{
    internal sealed class Checkout(IOrderRepository repository, IMetrics metrics)
    {
        public Result Place(Order order)
        {
            Result saved = repository.Save(order);              // held in a local: not reported
            saved.TapError(e => metrics.RecordFailure(e.Code)); // a side effect on a held result: not reported
            if (saved.IsFailure)
                return saved;

            return repository.Publish(order);                   // returned: the caller handles it
        }
    }

    // A base type that overrides Category settles it for its subtypes
    public abstract record PaymentError(ErrorCode Code, string Message) : Error(Code, Message)
    {
        public override ErrorCategory Category => ErrorCategory.BusinessRule;
    }

    public sealed record CardExpiredError() : PaymentError(ErrorCode.From("PAYMENT.CARD.EXPIRED"), "The card has expired.");

    // An internal fault says so explicitly
    public sealed record LedgerCorruptedError() : Error(ErrorCode.From("LEDGER.CORRUPTED"), "An internal error occurred.")
    {
        public override ErrorCategory Category => ErrorCategory.Technical;
    }

    internal sealed record Order;

    internal interface IOrderRepository
    {
        Result Save(Order order);

        Result Publish(Order order);
    }

    internal interface IMetrics
    {
        void RecordFailure(ErrorCode code);
    }
}
