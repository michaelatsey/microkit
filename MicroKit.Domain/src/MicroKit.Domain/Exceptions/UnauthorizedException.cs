// -----------------------------------------------------------------------
// <copyright file="UnauthorizedException.cs" company="Coreal">
// Copyright (c) Coreal. All rights reserved.All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>
// -----------------------------------------------------------------------

namespace MicroKit.Domain.Exceptions;

/// <summary>
/// Représente une exception d'autorisation dans le domaine.
/// </summary>
/// <seealso cref="Exception" />
public class UnauthorizedException : UnauthorizedAccessException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NotFoundException" /> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public UnauthorizedException(string message) : base(message) { }


    /// <summary>
    /// Initializes a new instance of the <see cref="UnauthorizedException"/> class.
    /// </summary>
    public UnauthorizedException()
        : base("The request requires user authentication")
    {
    }
}
