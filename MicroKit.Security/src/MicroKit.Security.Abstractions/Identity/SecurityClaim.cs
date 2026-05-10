namespace MicroKit.Security.Abstractions.Identity;

/// <summary>
/// Représente un claim de sécurité immuable et AOT-compatible.
/// Structure optimisée pour éviter les allocations sur le heap.
/// </summary>
/// <param name="Type">Le type du claim (ex: "role", "sub", "email").</param>
/// <param name="Value">La valeur du claim.</param>
public readonly record struct SecurityClaim(string Type, string Value)
{
    /// <summary>
    /// Claim vide représentant l'absence de valeur.
    /// </summary>
    public static SecurityClaim Empty => new(string.Empty, string.Empty);

    /// <summary>
    /// Indique si le claim est vide (type non défini).
    /// </summary>
    public bool IsEmpty => string.IsNullOrEmpty(Type);

    /// <summary>
    /// Vérifie si le claim correspond au type spécifié.
    /// </summary>
    /// <param name="type">Le type à vérifier.</param>
    /// <returns>True si le type correspond, false sinon.</returns>
    public bool IsType(string type) => string.Equals(Type, type, StringComparison.Ordinal);

    /// <summary>
    /// Vérifie si le claim correspond au type et à la valeur spécifiés.
    /// </summary>
    /// <param name="type">Le type à vérifier.</param>
    /// <param name="value">La valeur à vérifier.</param>
    /// <returns>True si le type et la valeur correspondent, false sinon.</returns>
    public bool Matches(string type, string value) =>
        string.Equals(Type, type, StringComparison.Ordinal) &&
        string.Equals(Value, value, StringComparison.Ordinal);
}
