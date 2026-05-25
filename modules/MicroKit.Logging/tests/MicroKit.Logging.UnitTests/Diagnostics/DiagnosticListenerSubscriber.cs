using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace MicroKit.Logging.UnitTests.Diagnostics;

/// <summary>
/// Test helper that subscribes to a named DiagnosticListener and collects emitted events.
/// Dispose to unsubscribe and prevent cross-test pollution.
/// Tests using this helper must run sequentially — place them in [Collection("DiagnosticListener")].
/// </summary>
internal sealed class DiagnosticListenerSubscriber
    : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
{
    private readonly string _listenerName;
    private readonly List<(string EventName, object? Payload)> _events = new();
    private IDisposable? _allListenersSubscription;
    private IDisposable? _listenerSubscription;

    internal DiagnosticListenerSubscriber(string listenerName)
    {
        _listenerName = listenerName;
        _allListenersSubscription = DiagnosticListener.AllListeners.Subscribe(this);
    }

    internal IReadOnlyList<(string EventName, object? Payload)> Events => _events;

    void IObserver<DiagnosticListener>.OnNext(DiagnosticListener listener)
    {
        if (listener.Name == _listenerName)
            _listenerSubscription = listener.Subscribe(this);
    }
    void IObserver<DiagnosticListener>.OnError(Exception error) { }
    void IObserver<DiagnosticListener>.OnCompleted() { }

    void IObserver<KeyValuePair<string, object?>>.OnNext(KeyValuePair<string, object?> value)
        => _events.Add((value.Key, value.Value));
    void IObserver<KeyValuePair<string, object?>>.OnError(Exception error) { }
    void IObserver<KeyValuePair<string, object?>>.OnCompleted() { }

    public void Dispose()
    {
        _listenerSubscription?.Dispose();
        _allListenersSubscription?.Dispose();
    }

    /// <summary>
    /// Reads an anonymous payload field by name via reflection. Only for test use.
    /// </summary>
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075",
        Justification = "Anonymous types used in diagnostic payloads always have their properties preserved by the compiler.")]
    internal static T? GetPayloadValue<T>(object? payload, string propertyName)
    {
        if (payload is null) return default;
        var prop = payload.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        return prop is null ? default : (T?)prop.GetValue(payload);
    }
}
