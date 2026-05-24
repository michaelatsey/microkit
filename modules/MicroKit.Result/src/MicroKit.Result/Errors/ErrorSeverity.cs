namespace MicroKit.Result;

/// <summary>
/// Indicates the severity level of an error.
/// </summary>
public enum ErrorSeverity
{
    /// <summary>Informational — does not indicate a problem.</summary>
    Information,

    /// <summary>A potential issue that may require attention.</summary>
    Warning,

    /// <summary>An error that prevented the operation from completing.</summary>
    Error,

    /// <summary>A critical error requiring immediate attention.</summary>
    Critical,
}
