namespace MicroKit.Messaging;

/// <summary>Why an inbox batch stopped before every claimed row was attempted.</summary>
public enum InboxBatchAbortReason
{
    /// <summary>Every claimed row was attempted.</summary>
    None = 0,

    /// <summary>
    /// A dependency the whole batch relies on was unreachable. Back off; it may recover on
    /// its own.
    /// </summary>
    DependencyUnavailable = 1,

    /// <summary>The host is shutting down. Not an error.</summary>
    Cancelled = 2,

    /// <summary>
    /// A required service is not registered. Will not recover without a redeployment, so the
    /// worker stops rather than backing off.
    /// </summary>
    ConfigurationError = 3,
}
