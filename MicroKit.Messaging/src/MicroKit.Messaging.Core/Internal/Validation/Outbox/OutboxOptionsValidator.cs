using MicroKit.Messaging.Core.Configuration;
using Microsoft.Extensions.Options;

namespace MicroKit.Messaging.Core.Internal.Validation.Outbox
{
    internal class OutboxOptionsValidator : IValidateOptions<OutboxOptions>
    {
        public ValidateOptionsResult Validate(string? name, OutboxOptions options)
        {
            var failures = new List<string>();

            // On ne valide que si l'Outbox est activée
            if (!options.Enabled) return ValidateOptionsResult.Success;

            // Validation des TimeSpan (non supportés par [Range])
            if (options.PollingInterval <= TimeSpan.Zero)
                failures.Add($"{nameof(options.PollingInterval)} must be greater than zero.");

            if (options.RetryDelay <= TimeSpan.Zero)
                failures.Add($"{nameof(options.RetryDelay)} must be greater than zero.");

            if (options.MessageExpiration <= TimeSpan.Zero)
                failures.Add($"{nameof(options.MessageExpiration)} must be greater than zero.");

            if (options.CleanupRunInterval <= TimeSpan.Zero)
                failures.Add($"{nameof(options.CleanupRunInterval)} must be greater than zero.");

            if (options.RetentionPeriod <= TimeSpan.Zero)
                failures.Add($"{nameof(options.RetentionPeriod)} must be greater than zero.");

            if (failures.Count != 0)
            {
                return ValidateOptionsResult.Fail(failures);
            }

            return ValidateOptionsResult.Success;
        }
    }
}
