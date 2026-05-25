namespace MicroKit.Logging.Internal;

/// <summary>
/// Array-backed mutable property bag passed to enrichers during scope creation.
/// Linear scan for property lookup — efficient for the small property counts typical in enrichment.
/// No allocation after construction; Reset() reuses the pre-allocated array.
/// </summary>
internal sealed class LogEnrichmentContext(int capacity) : ILogEnrichmentContext
{
    private readonly KeyValuePair<string, object?>[] _properties = new KeyValuePair<string, object?>[capacity];
    private int _count;

    public void SetProperty(string name, object? value)
    {
        // Update existing property in place
        for (int i = 0; i < _count; i++)
        {
            if (string.Equals(_properties[i].Key, name, StringComparison.Ordinal))
            {
                _properties[i] = new KeyValuePair<string, object?>(name, value);
                return;
            }
        }

        // Silently drop when at capacity (LoggingConstants.MaxPropertiesPerEnricher enforced by caller)
        if (_count < _properties.Length)
            _properties[_count++] = new KeyValuePair<string, object?>(name, value);
    }

    public bool TrySetProperty(string name, object? value)
    {
        for (int i = 0; i < _count; i++)
        {
            if (string.Equals(_properties[i].Key, name, StringComparison.Ordinal))
                return false;
        }

        if (_count < _properties.Length)
        {
            _properties[_count++] = new KeyValuePair<string, object?>(name, value);
            return true;
        }
        return false;
    }

    internal ReadOnlySpan<KeyValuePair<string, object?>> GetProperties()
        => new(_properties, 0, _count);

    internal int Count => _count;

    internal void Reset() => _count = 0;
}
