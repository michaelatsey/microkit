
using MicroKit.Security.Abstractions.Contexts;

namespace MicroKit.Security.Core.Services;
/// <summary>
/// Default implementation of client context accessor using AsyncLocal.
/// </summary>
public sealed class ClientContextAccessor : IClientContextAccessor
{
    private static readonly AsyncLocal<ClientContextHolder> _clientContextCurrent = new();

    /// <inheritdoc />
    public IClientContext? Context
    {
        get => _clientContextCurrent.Value?.Context;
        set
        {
            var holder = _clientContextCurrent.Value;
            if (holder is not null)
            {
                // Clear the current context trapped in the AsyncLocal
                holder.Context = null;
            }

            if (value is not null)
            {
                // Use a new holder to avoid issues with values flowing across requests
                _clientContextCurrent.Value = new ClientContextHolder { Context = value };
            }
        }
    }

    private sealed class ClientContextHolder
    {
        public IClientContext? Context;
    }
}
