using MicroKit.Security.Abstractions.Enums;

namespace MicroKit.Security.Abstractions.Extraction;

/// <summary>Result of an HTTP request credential extraction attempt.</summary>
/// <param name="Value">The raw credential (key or token) that was found.</param>
/// <param name="Scheme">The detected authentication scheme (e.g. <see cref="AuthenticationScheme.ApiKey"/>, <see cref="AuthenticationScheme.Jwt"/>).</param>
/// <param name="IsPrimaryCandidate">Whether this credential should be treated as the primary identity rather than a secondary signal.</param>
public record ExtractionResult(string? Value, AuthenticationScheme Scheme, bool IsPrimaryCandidate = true);
