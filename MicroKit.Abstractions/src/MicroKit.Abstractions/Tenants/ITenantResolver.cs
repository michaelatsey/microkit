using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Abstractions.Tenants;
/// <summary>
/// NOT IMPLEMENTED
/// </summary>
public interface ITenantResolver
{
    /// <summary>
    /// Résout l'identifiant du tenant actuel.
    /// </summary>
    /// <returns>L'identifiant du tenant, ou null si aucun tenant n'est résolu.</returns>
    string? Resolve();
}
