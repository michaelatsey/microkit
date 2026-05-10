using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Cqrs.Abstractions.Queries;

public interface IQueryBus
{
    Task<TResponse> AskAsync<TResponse>(IQuery<TResponse> query, CancellationToken ct = default);
}
