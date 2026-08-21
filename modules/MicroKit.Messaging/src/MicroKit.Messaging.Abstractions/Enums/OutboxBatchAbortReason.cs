namespace MicroKit.Messaging;

/// <summary>Why a batch stopped before every claimed message was attempted.</summary>
public enum OutboxBatchAbortReason
{
    /// <summary>Every claimed message was attempted.</summary>
    None = 0,

    /// <summary>The transport was unreachable. Back off hard; it may recover on its own.</summary>
    TransportUnavailable = 1,

    /// <summary>The host is shutting down. Not an error.</summary>
    Cancelled = 2,

    /// <summary>
    /// A required service is not registered. Will not recover without a redeployment,
    /// so the worker stops rather than backing off.
    /// </summary>
    ConfigurationError = 3,
}
