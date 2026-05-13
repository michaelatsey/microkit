using MicroKit.Security.Abstractions.Extraction;
using Microsoft.AspNetCore.Http;

namespace MicroKit.Security.AspNetCore.Extraction;

/// <summary>
/// Contract for extracting credentials from an HTTP transport.
/// </summary>
public interface IAuthenticationExtractor
{
    /// <summary>
    /// Execution priority — higher values run first.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Attempts to extract credentials from the current HTTP request.
    /// </summary>
    ValueTask<ExtractionResult> ExtractCredentialsAsync(HttpContext context);
}
