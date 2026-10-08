using System;
using UnityEngine;

namespace Velvet
{
    /// <summary>
    /// The application-wide readings <see cref="RetryPolicy"/> pauses on when a policy supplies none of its own
    /// (<see cref="RetryPolicy.IsOnline"/>, <see cref="RetryPolicy.IsFocused"/>): whether the device is online and
    /// whether the application is visible, TanStack's online manager and focus manager.
    /// </summary>
    /// <remarks>
    /// Set an override once at startup to replace the reading for every mutation and retry, such as an
    /// application that reaches a server on a local network <c>Application.internetReachability</c> reports
    /// as unreachable. An override is called on the main thread.
    /// </remarks>
    public static class NetworkSignals
    {
        /// <summary>
        /// Replaces the default online reading, <c>Application.internetReachability</c> is not
        /// <c>NotReachable</c>. Null restores the default.
        /// </summary>
        public static Func<bool>? IsOnline { get; set; }

        /// <summary>
        /// Replaces the default visibility reading, a read of the platform's window state. Null restores the
        /// default.
        /// </summary>
        public static Func<bool>? IsVisible { get; set; }

        internal static bool ReadOnline() =>
            IsOnline?.Invoke() ?? Application.internetReachability != NetworkReachability.NotReachable;

        internal static bool ReadVisible() => IsVisible?.Invoke() ?? ApplicationVisibility.IsVisible();
    }
}
