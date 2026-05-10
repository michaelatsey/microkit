using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Abstractions.Serialization;

/// <summary>
/// Représente un contrat pour la sérialisation et la désérialisation des messages dans le système de messagerie, permettant de convertir les objets en chaînes de caractères (généralement au format JSON) pour l'envoi et de reconvertir les chaînes de caractères en objets lors de la réception, facilitant ainsi la communication entre les différentes parties du système de messagerie tout en assurant la flexibilité et l'interopérabilité des données échangées.
/// </summary>
public interface IMicroKitSerializer
{
    /// <summary>
    /// Serializes the specified value.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="value">The value.</param>
    /// <returns></returns>
    string Serialize<T>(T value);
    /// <summary>
    /// Deserializes the specified json.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="json">The json.</param>
    /// <returns></returns>
    T? Deserialize<T>(string json);
    /// <summary>
    /// Deserializes the specified json.
    /// </summary>
    /// <param name="json">The json.</param>
    /// <param name="type">The type.</param>
    /// <returns></returns>
    object? Deserialize(string json, Type type);
}
