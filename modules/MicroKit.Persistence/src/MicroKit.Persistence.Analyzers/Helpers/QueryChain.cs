using Microsoft.CodeAnalysis.Text;

namespace MicroKit.Persistence.Analyzers.Helpers;

/// <summary>
/// Walks a fluent query chain in the operation tree: the receiver of each call (the instance, or the
/// extension method's first parameter), the call that takes an operation as its receiver, the chain's root
/// and its end. Implicit conversions (<c>DbSet</c> to <c>IQueryable</c>, <c>List</c> to <c>IEnumerable</c>)
/// are stepped over; C# has no parenthesized operation.
/// </summary>
internal static class QueryChain
{
    /// <summary>Strips the implicit conversions around <paramref name="operation"/>.</summary>
    public static IOperation Strip(IOperation operation)
    {
        while (operation is IConversionOperation { IsImplicit: true } conversion)
            operation = conversion.Operand;
        return operation;
    }

    /// <summary>The receiver of <paramref name="invocation"/>: its instance, or its extension method's <c>this</c> argument.</summary>
    public static IOperation? ReceiverOf(IInvocationOperation invocation)
    {
        if (invocation.Instance is { } instance)
            return Strip(instance);
        if (!invocation.TargetMethod.IsExtensionMethod)
            return null;
        foreach (var argument in invocation.Arguments)
        {
            // By parameter, not position: named arguments in the static call form reorder Arguments.
            if (argument.Parameter?.Ordinal == 0)
                return Strip(argument.Value);
        }

        return null;
    }

    /// <summary>The invocation whose receiver is <paramref name="operation"/>, if any.</summary>
    public static IInvocationOperation? NextOf(IOperation operation)
    {
        var child = operation;
        var parent = operation.Parent;
        while (parent is IConversionOperation { IsImplicit: true })
        {
            child = parent;
            parent = parent.Parent;
        }

        if (parent is IInvocationOperation call && call.Instance == child)
            return call;
        if (parent is IArgumentOperation { Parameter.Ordinal: 0 } argument &&
            argument.Parent is IInvocationOperation extension &&
            extension.TargetMethod.IsExtensionMethod)
        {
            return extension;
        }

        return null;
    }

    /// <summary>
    /// The root of the query chain <paramref name="operation"/> belongs to: walks receivers down while they are
    /// queryable. For <c>ctx.Orders.Where(…).Include(…)</c> it is <c>ctx.Orders</c>; for <c>ctx.Set&lt;T&gt;()</c>,
    /// the <c>Set</c> call.
    /// </summary>
    public static IOperation RootOf(IOperation operation, QuerySymbols symbols)
    {
        operation = Strip(operation);
        while (operation is IInvocationOperation invocation &&
               ReceiverOf(invocation) is { } receiver &&
               symbols.IsQueryable(receiver.Type))
        {
            operation = receiver;
        }

        return operation;
    }

    /// <summary>The calls of the chain from <paramref name="root"/> (excluded) up to <paramref name="last"/> (included), in order.</summary>
    public static List<IInvocationOperation> CallsBetween(IOperation root, IOperation last)
    {
        var calls = new List<IInvocationOperation>();
        for (var current = Strip(last); current is IInvocationOperation invocation && current != root;)
        {
            calls.Add(invocation);
            if (ReceiverOf(invocation) is not { } receiver)
                break;
            current = receiver;
        }

        calls.Reverse();
        return calls;
    }

    /// <summary>
    /// The chain ending at <paramref name="end"/>, innermost first: the bottom operation (the first receiver
    /// that is not a call with a receiver), then each call up to <paramref name="end"/>.
    /// </summary>
    public static List<IOperation> Flatten(IOperation end)
    {
        var chain = new List<IOperation>();
        var current = Strip(end);
        while (true)
        {
            chain.Add(current);
            if (current is IInvocationOperation invocation && ReceiverOf(invocation) is { } receiver)
                current = receiver;
            else
                break;
        }

        chain.Reverse();
        return chain;
    }

    /// <summary>The index of the first queryable operation of <paramref name="chain"/>, or -1.</summary>
    public static int QueryRootIndex(List<IOperation> chain, QuerySymbols symbols)
    {
        for (var i = 0; i < chain.Count; i++)
        {
            if (symbols.IsQueryable(chain[i].Type))
                return i;
        }

        return -1;
    }

    /// <summary>The parent of <paramref name="operation"/>, past the implicit conversions around it.</summary>
    public static IOperation? ParentOf(IOperation operation, out IOperation child)
    {
        child = operation;
        var parent = operation.Parent;
        while (parent is IConversionOperation { IsImplicit: true })
        {
            child = parent;
            parent = parent.Parent;
        }

        return parent;
    }

    /// <summary>
    /// Whether <paramref name="operation"/> is the collection a <c>foreach</c> or <c>await foreach</c> enumerates,
    /// through <c>AsAsyncEnumerable()</c>, <c>WithCancellation(…)</c> and <c>ConfigureAwait(…)</c>.
    /// </summary>
    public static bool IsEnumeratedByLoop(IOperation operation, out IForEachLoopOperation? loop)
    {
        var current = operation;
        while (true)
        {
            var parent = ParentOf(current, out var child);
            if (parent is IForEachLoopOperation forEach && forEach.Collection == child)
            {
                loop = forEach;
                return true;
            }

            if (NextOf(current) is { TargetMethod.Name: "AsAsyncEnumerable" or "WithCancellation" or "ConfigureAwait" or "AsEnumerable" } next)
            {
                current = next;
                continue;
            }

            loop = null;
            return false;
        }
    }

    /// <summary>The outermost call of the chain <paramref name="operation"/> belongs to.</summary>
    public static IOperation EndOf(IOperation operation)
    {
        while (NextOf(operation) is { } next)
            operation = next;
        return operation;
    }

    /// <summary>Steps through <c>await x</c> and <c>await x.ConfigureAwait(…)</c> to <c>x</c>.</summary>
    public static IOperation UnwrapAwait(IOperation operation)
    {
        operation = Strip(operation);
        if (operation is not IAwaitOperation awaited)
            return operation;
        var inner = Strip(awaited.Operation);
        if (inner is IInvocationOperation { TargetMethod.Name: "ConfigureAwait", Instance: { } configured })
            inner = Strip(configured);
        return inner;
    }

    /// <summary>The location from the invoked method's name to the end of its argument list.</summary>
    public static Location NameLocation(IOperation operation)
    {
        if (operation.Syntax is InvocationExpressionSyntax invocation)
        {
            var start = invocation.Expression switch
            {
                MemberAccessExpressionSyntax member  => member.Name.SpanStart,
                MemberBindingExpressionSyntax member => member.Name.SpanStart,
                _                                    => invocation.Expression.SpanStart,
            };
            return Location.Create(invocation.SyntaxTree, TextSpan.FromBounds(start, invocation.Span.End));
        }

        return operation.Syntax.GetLocation();
    }
}
