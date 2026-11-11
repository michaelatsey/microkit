namespace MicroKit.Messaging.Options;

using Microsoft.Extensions.Options;

/// <summary>
/// Validates <see cref="MessagingOptions"/> at host start (<c>ValidateOnStart</c>). Every failure
/// names its configuration key and the accepted range.
/// </summary>
/// <remarks>
/// <para>
/// An <see cref="IValidateOptions{TOptions}"/> rather than data annotations: annotation validation
/// reads the attributes by reflection, which a trimmable library cannot rely on.
/// </para>
/// <para><b>Where each upper bound comes from.</b></para>
/// <list type="bullet">
/// <item><description>
/// The intervals the workers wait for — <c>PollingInterval</c>, <c>MaxPollingInterval</c>,
/// <c>TransportUnavailableBackoff</c>, <c>DependencyUnavailableBackoff</c>,
/// <c>RetentionInterval</c> — reach <c>Task.Delay(TimeSpan, CancellationToken)</c>, which throws
/// <see cref="ArgumentOutOfRangeException"/> when <c>TotalMilliseconds</c> exceeds 4294967294 on
/// .NET 6 and later. The workers pass either <c>PollingInterval</c> or a value doubled from a
/// delay and then clamped to one of the other four, so bounding all five bounds every delay; the
/// doubling of a value at most 4294967294 ms cannot overflow <see cref="TimeSpan"/>.
/// </description></item>
/// <item><description>
/// <c>OutcomeFlushTimeout</c> reaches <c>new CancellationTokenSource(TimeSpan)</c>, documented to
/// throw above <see cref="int.MaxValue"/> milliseconds (or <see cref="uint.MaxValue"/> − 1 on some
/// versions of .NET). The stricter of the two is the bound.
/// </description></item>
/// <item><description>
/// <c>Outbox:LockDuration</c> and <c>Inbox:LeaseDuration</c> are added to the current time by the
/// store's claim (<see cref="DateTimeOffset.Add(TimeSpan)"/>), which throws
/// <see cref="ArgumentOutOfRangeException"/> past <see cref="DateTimeOffset.MaxValue"/>. A positive
/// <c>RetentionDays</c> is subtracted from it by the retention worker
/// (<see cref="DateTimeOffset.AddDays(double)"/>), which throws past
/// <see cref="DateTimeOffset.MinValue"/>; zero or less disables retention, as documented, and never
/// reaches that call. Both bounds are measured from the clock at validation — thousands of years
/// away, so a value that passes keeps passing.
/// </description></item>
/// <item><description>
/// No other value has an upper bound: none reaches an API that documents one.
/// <c>MaxRetryBackoff</c> only caps a back-off the processors already clamp to 2^20 seconds.
/// </description></item>
/// </list>
/// </remarks>
/// <param name="timeProvider">The clock the stores and workers read.</param>
internal sealed class MessagingOptionsValidator(TimeProvider timeProvider) : IValidateOptions<MessagingOptions>
{
    /// <summary>The longest interval <c>Task.Delay(TimeSpan, CancellationToken)</c> accepts.</summary>
    internal static readonly TimeSpan MaxDelay = TimeSpan.FromMilliseconds(4294967294L);

    /// <summary>The longest timeout <c>new CancellationTokenSource(TimeSpan)</c> accepts on every version.</summary>
    internal static readonly TimeSpan MaxCancellationDelay = TimeSpan.FromMilliseconds(int.MaxValue);

    private const string Section = MessagingOptions.SectionName;

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, MessagingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        var now = timeProvider.GetUtcNow();

        if (string.IsNullOrWhiteSpace(options.ServiceName))
        {
            failures.Add(
                $"The service name is missing: set bus.ServiceName or {Section}:ServiceName. It names " +
                "the service's endpoint and is the source of every message it publishes.");
        }

        if (options.Outbox is null)
        {
            failures.Add($"{Section}:Outbox is null. Leave it unset to keep the defaults.");
        }
        else
        {
            var o = options.Outbox;
            var p = $"{Section}:Outbox";
            AtLeastOne(failures, $"{p}:BatchSize", o.BatchSize);
            Delay(failures, $"{p}:PollingInterval", o.PollingInterval);
            Delay(failures, $"{p}:MaxPollingInterval", o.MaxPollingInterval);
            Ordered(failures, $"{p}:PollingInterval", o.PollingInterval, $"{p}:MaxPollingInterval", o.MaxPollingInterval);
            Delay(failures, $"{p}:TransportUnavailableBackoff", o.TransportUnavailableBackoff);
            Lease(failures, $"{p}:LockDuration", o.LockDuration, now);
            AtLeastOne(failures, $"{p}:MaxRetries", o.MaxRetries);
            Positive(failures, $"{p}:MaxRetryBackoff", o.MaxRetryBackoff);
            Timeout(failures, $"{p}:OutcomeFlushTimeout", o.OutcomeFlushTimeout);
            AtLeastOne(failures, $"{p}:MaxErrorMessageLength", o.MaxErrorMessageLength);
            Delay(failures, $"{p}:RetentionInterval", o.RetentionInterval);
            Retention(failures, $"{p}:RetentionDays", o.RetentionDays, now);
        }

        if (options.Inbox is null)
        {
            failures.Add($"{Section}:Inbox is null. Leave it unset to keep the defaults.");
        }
        else
        {
            var i = options.Inbox;
            var p = $"{Section}:Inbox";
            AtLeastOne(failures, $"{p}:BatchSize", i.BatchSize);
            Delay(failures, $"{p}:PollingInterval", i.PollingInterval);
            Delay(failures, $"{p}:MaxPollingInterval", i.MaxPollingInterval);
            Ordered(failures, $"{p}:PollingInterval", i.PollingInterval, $"{p}:MaxPollingInterval", i.MaxPollingInterval);
            Delay(failures, $"{p}:DependencyUnavailableBackoff", i.DependencyUnavailableBackoff);
            Lease(failures, $"{p}:LeaseDuration", i.LeaseDuration, now);
            AtLeastOne(failures, $"{p}:MaxRetries", i.MaxRetries);
            Positive(failures, $"{p}:MaxRetryBackoff", i.MaxRetryBackoff);
            Timeout(failures, $"{p}:OutcomeFlushTimeout", i.OutcomeFlushTimeout);
            AtLeastOne(failures, $"{p}:MaxErrorMessageLength", i.MaxErrorMessageLength);
            Delay(failures, $"{p}:RetentionInterval", i.RetentionInterval);
            Retention(failures, $"{p}:RetentionDays", i.RetentionDays, now);
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void AtLeastOne(List<string> failures, string key, int value)
    {
        if (value < 1)
        {
            failures.Add($"{key} must be at least 1; it is {value}.");
        }
    }

    private static void Positive(List<string> failures, string key, TimeSpan value)
    {
        if (value <= TimeSpan.Zero)
        {
            failures.Add($"{key} must be greater than zero; it is {value}.");
        }
    }

    private static void Lease(List<string> failures, string key, TimeSpan value, DateTimeOffset now)
    {
        var max = DateTimeOffset.MaxValue - now;
        if (value <= TimeSpan.Zero || value > max)
        {
            failures.Add(
                $"{key} must be greater than zero and at most {max} (added to the current time, it " +
                $"must not pass DateTimeOffset.MaxValue); it is {value}.");
        }
    }

    private static void Retention(List<string> failures, string key, int value, DateTimeOffset now)
    {
        var max = (int)Math.Min(int.MaxValue, Math.Floor((now - DateTimeOffset.MinValue).TotalDays));
        if (value > max)
        {
            failures.Add(
                $"{key} must be at most {max} (subtracted from the current time, it must not pass " +
                $"DateTimeOffset.MinValue); zero or less disables retention; it is {value}.");
        }
    }

    private static void Delay(List<string> failures, string key, TimeSpan value)
    {
        if (value <= TimeSpan.Zero || value > MaxDelay)
        {
            failures.Add(
                $"{key} must be greater than zero and at most {MaxDelay} (4294967294 ms, the " +
                $"longest Task.Delay accepts); it is {value}.");
        }
    }

    private static void Timeout(List<string> failures, string key, TimeSpan value)
    {
        if (value <= TimeSpan.Zero || value > MaxCancellationDelay)
        {
            failures.Add(
                $"{key} must be greater than zero and at most {MaxCancellationDelay} " +
                $"({int.MaxValue} ms, the longest CancellationTokenSource timeout accepted on every " +
                $"version of .NET); it is {value}.");
        }
    }

    private static void Ordered(
        List<string> failures, string lowKey, TimeSpan low, string highKey, TimeSpan high)
    {
        if (low > high)
        {
            failures.Add(
                $"{lowKey} ({low}) must not exceed {highKey} ({high}): the polling interval grows " +
                "from the first toward the second.");
        }
    }
}
