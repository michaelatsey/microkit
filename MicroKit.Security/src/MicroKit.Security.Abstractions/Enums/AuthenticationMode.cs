namespace MicroKit.Security.Abstractions.Enums;

/// <summary>Controls how multiple credential sources are evaluated during a single request.</summary>
public enum AuthenticationMode
{
    /// <summary>The first provider to return a successful result wins; remaining providers are skipped.</summary>
    FirstSuccess = 0,

    /// <summary>Exactly one credential must be present in the request; multiple credentials are rejected.</summary>
    StrictSingleCredential = 1
}
