using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Messaging.Abstractions.Inbox;

/// <summary>Registry of known inbox consumer names used to route incoming messages.</summary>
public interface IInboxConsumerRegistry
{
    /// <summary>Returns the names of all registered inbox consumers.</summary>
    IReadOnlyList<string> GetConsumerNames();
}
