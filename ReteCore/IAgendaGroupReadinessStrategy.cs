//-----------------------------------------------------------------------
// <copyright file="IAgendaGroupReadinessStrategy.cs">
//     Copyright (c) Steven Orton. All rights reserved.
//     Licensed under the GNU Lesser General Public License v2.1.
//     See LICENSE file in the ReteRaven project root for full license
//     information.
// </copyright>
//-----------------------------------------------------------------------
namespace ReteCore
{
    /// <summary>
    /// Determines whether an agenda group is currently allowed to fire activations.
    /// </summary>
    public interface IAgendaGroupReadinessStrategy
    {
        bool CanResume();
    }
}
