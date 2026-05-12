using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Cqrs.Abstractions.Queries;

/// <summary>Dispatches queries to their registered handlers and returns their results.</summary>
public interface IQueryBus
{
    /// <summary>Dispatches the given query and returns its result.</summary>
    /// <typeparam name="TResponse">The response type produced by the query handler.</typeparam>
    /// <param name="query">The query to dispatch.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The query result.</returns>
    Task<TResponse> AskAsync<TResponse>(IQuery<TResponse> query, CancellationToken ct = default);
}
