using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace MicroKit.Messaging.UnitTests.Processing;

/// <summary>
/// Records the measurements of the given <see cref="Meter"/> instances only — never every meter of a
/// name: tests run in parallel, and each builds its own <c>OutboxMetrics</c> or <c>InboxMetrics</c>.
/// </summary>
internal sealed class MeterRecorder : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly ConcurrentQueue<Recorded> _recorded = new();
    private readonly ConcurrentQueue<string> _published = new();

    public MeterRecorder(params Meter[] meters)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (Array.IndexOf(meters, instrument.Meter) >= 0)
            {
                _published.Enqueue(instrument.Name);
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((i, v, tags, _) => Add(i, v, tags));
        _listener.SetMeasurementEventCallback<int>((i, v, tags, _) => Add(i, v, tags));
        _listener.SetMeasurementEventCallback<double>((i, v, tags, _) => Add(i, v, tags));
        _listener.Start();
    }

    /// <summary>Gets the names of the instruments published on the listened meters.</summary>
    public IReadOnlyList<string> Published => [.. _published];

    /// <summary>The measurements of <paramref name="instrument"/> recorded so far.</summary>
    public List<Recorded> Of(string instrument) => [.. _recorded.Where(r => r.Instrument == instrument)];

    /// <summary>Runs the observable callbacks once and returns what <paramref name="instrument"/> observed.</summary>
    public List<Recorded> Observe(string instrument)
    {
        _recorded.Clear();
        _listener.RecordObservableInstruments();
        return Of(instrument);
    }

    public void Dispose() => _listener.Dispose();

    private void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var copy = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            copy[tag.Key] = tag.Value;
        }

        _recorded.Enqueue(new Recorded(instrument.Name, value, copy));
    }

    internal sealed record Recorded(string Instrument, double Value, IReadOnlyDictionary<string, object?> Tags)
    {
        public string? Tag(string key) => Tags.TryGetValue(key, out var value) ? value as string : null;
    }
}
