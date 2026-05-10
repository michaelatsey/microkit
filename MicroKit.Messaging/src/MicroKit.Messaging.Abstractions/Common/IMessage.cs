
using MicroKit.Events.Contracts;

namespace MicroKit.Messaging.Abstractions.Common;

/// <summary>
/// Interface de base pour tous les messages
/// </summary>
public interface IMessage //: IEvent
{
    string Payload { get; }
    
}
