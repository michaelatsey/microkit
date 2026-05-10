using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Events.Contracts;

public interface IIntegrationEvent
{
    Guid Id { get; }
    DateTimeOffset OccurredOnUtc { get; }
}
