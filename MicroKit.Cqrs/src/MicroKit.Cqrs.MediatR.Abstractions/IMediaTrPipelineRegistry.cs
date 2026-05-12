using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Cqrs.MediatR.Abstractions;

/// <summary>Registry for MediatR pipeline behavior types ordered for pipeline construction.</summary>
public interface IMediaTrPipelineRegistry
{
    /// <summary>Gets the ordered list of pipeline behavior types registered for the MediatR pipeline.</summary>
    List<Type> MiddleBehaviors { get; }
}
