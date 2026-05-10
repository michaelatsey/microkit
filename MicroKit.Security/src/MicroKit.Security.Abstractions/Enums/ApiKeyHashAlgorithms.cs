namespace MicroKit.Security.Abstractions.Enums;

/// <summary>
/// Définit les algorithmes de hachage supportés pour le stockage des clés API.
/// </summary>
public enum ApiKeyHashAlgorithms
{
    /// <summary>
    /// SHA-256 (Standard, excellent compromis performance/sécurité).
    /// </summary>
    SHA256 = 0,

    /// <summary>
    /// SHA-512 (Plus lent, mais plus résistant aux collisions/attaques par force brute sur processeurs 64-bit).
    /// </summary>
    SHA512 = 1
}
