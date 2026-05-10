using MicroKit.Security.Abstractions.Identity;
using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Security.Abstractions.Authorization;

public interface IAuthorizationService
{
    /// <summary>
    /// Vérifie si le principal possède les permissions nécessaires.
    /// </summary>
    /// <param name="principal">L'identité à vérifier.</param>
    /// <param name="permissions">Liste des permissions (OR logic : au moins une requise).</param>
    bool IsAuthorized(ISecurityPrincipal principal, params string[] permissions);

    /// <summary>
    /// Vérifie si le principal possède TOUTES les permissions spécifiées.
    /// </summary>
    bool HasAllPermissions(ISecurityPrincipal principal, params string[] permissions);
}
