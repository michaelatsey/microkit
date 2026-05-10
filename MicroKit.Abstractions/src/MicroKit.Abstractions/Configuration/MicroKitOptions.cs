using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Abstractions.Configuration
{
    /// <summary>
    /// Réprésente les options de configuration pour le système de messagerie MicroKit, regroupant les paramètres liés à la boîte d'envoi (Outbox), à la boîte de réception (Inbox) et à la sérialisation des messages, permettant de personnaliser le comportement du système de messagerie en fonction des besoins spécifiques de l'application, tels que l'activation ou la désactivation des fonctionnalités, la définition des intervalles de traitement, les stratégies de réessai et les options de sérialisation utilisées pour convertir les messages en format JSON et vice versa.
    /// </summary>
    public class MicroKitOptions
    {
        /// <summary>
        /// Gets or sets the serialization.
        /// </summary>
        /// <value>
        /// The serialization.
        /// </value>
        public SerializationOptions Serialization { get; set; } = new();
    }
}
