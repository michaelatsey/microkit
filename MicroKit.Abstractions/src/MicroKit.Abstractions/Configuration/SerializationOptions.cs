using System.Text.Json;

namespace MicroKit.Abstractions.Configuration;

/// <summary>
/// Réprésente les options de configuration pour la sérialisation des messages dans le système de messagerie, permettant de définir les paramètres liés à la sérialisation et à la désérialisation des messages, tels que le type de contenu par défaut et les options du sérialiseur JSON utilisé pour convertir les messages en format JSON et vice versa.
/// </summary>
public class SerializationOptions
{
    /// <summary>
    /// Gets or sets the default type of the content.
    /// </summary>
    /// <value>
    /// The default type of the content.
    /// </value>
    public string DefaultContentType { get; set; } = "application/json";
    /// <summary>
    /// Gets or sets the json serializer options.
    /// </summary>
    /// <value>
    /// The json serializer options.
    /// </value>
    public JsonSerializerOptions? JsonSerializerOptions { get; set; }
}
