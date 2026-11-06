//-----------------------------------------------------------------------
// <copyright file="AgendaFocusStackChangedEventArgs.cs">
//     Copyright (c) Steven Orton. All rights reserved.
//     Licensed under the GNU Lesser General Public License v2.1.
//     See LICENSE file in the ReteRaven project root for full license
//     information.
// </copyright>
//-----------------------------------------------------------------------
namespace ReteCore
{
    /// <summary>
    /// Identifies how the agenda focus stack changed.
    /// </summary>
    public enum AgendaFocusStackChangeReason
    {
        GroupFocused,
        GroupPopped,
        GroupAutomaticallyPopped
    }

    /// <summary>
    /// Provides immutable details about a change to the agenda focus stack.
    /// </summary>
    public sealed class AgendaFocusStackChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Constructs a new instance of the <see cref="AgendaFocusStackChangedEventArgs"/> class.
        /// </summary>
        /// <param name="changedAt">The time at which the focus stack changed.</param>
        /// <param name="previousStack">The focus stack before the change, ordered from base to focused group.</param>
        /// <param name="currentStack">The focus stack after the change, ordered from base to focused group.</param>
        /// <param name="reason">The reason for the focus stack change.</param>
        internal AgendaFocusStackChangedEventArgs(
            DateTimeOffset changedAt,
            IReadOnlyList<string> previousStack,
            IReadOnlyList<string> currentStack,
            AgendaFocusStackChangeReason reason)
        {
            ChangedAt = changedAt;
            PreviousStack = Array.AsReadOnly(previousStack.ToArray());
            CurrentStack = Array.AsReadOnly(currentStack.ToArray());
            Reason = reason;
        }

        /// <summary>
        /// Gets the time at which the focus stack changed.
        /// </summary>
        public DateTimeOffset ChangedAt { get; }

        /// <summary>
        /// Gets the focus stack before the change, ordered from base to focused group.
        /// </summary>
        public IReadOnlyList<string> PreviousStack { get; }

        /// <summary>
        /// Gets the focus stack after the change, ordered from base to focused group.
        /// </summary>
        public IReadOnlyList<string> CurrentStack { get; }

        /// <summary>
        /// Gets the reason for the focus stack change.
        /// </summary>
        public AgendaFocusStackChangeReason Reason { get; }
    }
}
