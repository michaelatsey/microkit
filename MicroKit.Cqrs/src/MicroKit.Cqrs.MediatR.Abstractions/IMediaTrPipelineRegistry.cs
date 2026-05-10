using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Cqrs.MediatR.Abstractions;

public interface IMediaTrPipelineRegistry
{
    List<Type> MiddleBehaviors { get; }
}
