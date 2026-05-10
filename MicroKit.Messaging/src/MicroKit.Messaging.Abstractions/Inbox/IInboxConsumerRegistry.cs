using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Messaging.Abstractions.Inbox;

public interface IInboxConsumerRegistry
{
    IReadOnlyList<string> GetConsumerNames();
}
