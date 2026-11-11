namespace MicroKit.Messaging.Options;

/// <summary>
/// The options of MicroKit.Messaging: the service name and the outbox and inbox engine settings.
/// Bound from the <c>MicroKit:Messaging</c> configuration section and validated at host start.
/// </summary>
/// <remarks>
/// <para>
/// <b>Precedence, lowest first:</b> the host's <c>IHostEnvironment.ApplicationName</c> (for
/// <see cref="ServiceName"/> only, and only when nothing else set it), then configuration, then
/// <c>bus.Configure(...)</c>, then <c>bus.ServiceName</c>.
/// </para>
/// <para>
/// Configuration is read <b>once</b>, when the options are first resolved. There is no reload: a
/// changed value takes effect on the next start.
/// </para>
/// </remarks>
public sealed record MessagingOptions
{
    /// <summary>The configuration section the options are bound from.</summary>
    public const string SectionName = "MicroKit:Messaging";

    /// <summary>
    /// Gets or sets the name of this service. It is the <c>Source</c> of every message the bus
    /// publishes and the name of the endpoint the service consumes from. Required: a missing or
    /// blank value fails at host start.
    /// </summary>
    public string? ServiceName { get; set; }

    /// <summary>Gets or sets the outbox engine options (section <c>MicroKit:Messaging:Outbox</c>).</summary>
    public OutboxProcessorOptions Outbox { get; set; } = new();

    /// <summary>Gets or sets the inbox engine options (section <c>MicroKit:Messaging:Inbox</c>).</summary>
    public InboxProcessorOptions Inbox { get; set; } = new();
}
