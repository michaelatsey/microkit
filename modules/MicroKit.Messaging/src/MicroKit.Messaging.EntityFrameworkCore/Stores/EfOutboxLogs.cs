using Microsoft.Extensions.Logging;

namespace MicroKit.Messaging.EntityFrameworkCore;

/// <summary>Source-generated log methods of the EF Core store.</summary>
internal static partial class EfOutboxLogs
{
    [LoggerMessage(
        EventId = 4000,
        Level = LogLevel.Error,
        Message = "Message {MessageId} ('{Contract}') was enlisted in the outbox of store '{Store}', " +
                  "and its scope ended before the unit of work was saved: it is lost. Save the " +
                  "DbContext the publication joined — SaveChanges, or the unit of work that owns it.")]
    public static partial void UnsavedEnlistment(ILogger logger, Guid messageId, string contract, string store);

    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Debug,
        Message = "Message {MessageId} ('{Contract}') enlisted in the outbox of store '{Store}' was not " +
                  "in the change tracker as saved when its scope ended: discarded with the work it announced " +
                  "(a rolled-back attempt, or a tracker cleared before the save), or saved and then cleared " +
                  "from the tracker, in which case it is published at the next poll if its save committed.")]
    public static partial void DiscardedEnlistment(ILogger logger, Guid messageId, string contract, string store);
}
