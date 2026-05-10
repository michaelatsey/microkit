using MicroKit.Resilience.Abstractions;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;

namespace MicroKit.Resilience.Http
{
    public class HttpResilienceDetector : IResilienceStrategyDetector
    {
        // On gère les exceptions HTTP et les problèmes réseau bas niveau (Socket)
        public bool CanHandle(Exception ex) =>
            ex is HttpRequestException ||
            ex is TaskCanceledException || // Souvent le signe d'un timeout
            ex is SocketException;

        public bool ShouldRetry(Exception ex)
        {
            return ex switch
            {
                // Cas 1 : Erreur HTTP avec un code de statut spécifique
                HttpRequestException httpEx when httpEx.StatusCode is null || (int)httpEx.StatusCode >= 500
                    => true, // On réessaye si le serveur distant est en vrac (5xx) ou si la connexion a échoué

                // Cas 2 : Timeout (TaskCanceledException qui n'est pas dû à l'utilisateur)
                TaskCanceledException tce when !tce.CancellationToken.IsCancellationRequested
                    => true,

                // Cas 3 : Erreur de socket (Réseau coupé, DNS, etc.)
                SocketException => true,

                _ => false
            };
        }
    }
}
