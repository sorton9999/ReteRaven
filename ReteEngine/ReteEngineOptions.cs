// <copyright file="ReteEngineOptions.cs">
//     Copyright (c) Steven Orton. All rights reserved.
//     Licensed under the GNU Lesser General Public License v2.1.
//     See LICENSE file in the ReteRaven project root for full license
//     information.
// </copyright>
//-----------------------------------------------------------------------
namespace ReteEngine
{
    /// <summary>
    /// Configures behavior of a ReteEngine instance.
    /// </summary>
    public sealed class ReteEngineOptions
    {
        /// <summary>
        /// Gets or sets whether compatible beta nodes may be shared across rules.
        /// </summary>
        public bool EnableBetaNodeSharing { get; init; } = true;

        /// <summary>
        /// Gets or sets whether the engine subscribes to INotifyPropertyChanged facts
        /// and refreshes the network automatically when those facts change.
        /// When disabled, callers must explicitly call Update or Refresh after a change.
        /// This setting does not prevent the fact object itself from being mutated.
        /// </summary>
        public bool AutoRefreshOnPropertyChanged { get; init; } = true;
    }
}
