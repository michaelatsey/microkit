
using MicroKit.Events.Contracts;

namespace MicroKit.Messaging.Abstractions.Common;

/// <summary>Base contract for all serialized messages flowing through the outbox/inbox pipeline.</summary>
public interface IMessage //: IEvent
{
    /// <summary>Gets the serialized JSON payload of the message.</summary>
    string Payload { get; }

}
