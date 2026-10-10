using System;
using UnityEngine;

namespace Velvet
{
    /// <summary>
    /// The application-wide readings of whether the device is online and whether the application is visible,
    /// TanStack's online manager and focus manager. A <see cref="QueryClient"/> refetches its queries when either
    /// turns true (<c>RefetchOnReconnect</c>, <c>RefetchOnWindowFocus</c>), and a refetch interval waits while
    /// the application is not visible (<c>RefetchIntervalInBackground</c>).
    /// </summary>
    /// <remarks>
    /// Set an override once at startup to replace the reading for every query, for an application that
    /// measures being online for itself or tells when its window is hidden. An override is called on the main thread.
    /// </remarks>
    public static class NetworkSignals
    {
        /// <summary>
        /// Replaces the default online reading, <c>Application.internetReachability</c> is not
        /// <c>NotReachable</c>. Null restores the default.
        /// </summary>
        public static Func<bool>? IsOnline { get; set; }

        /// <summary>
        /// Replaces the default visibility reading, <c>Application.isFocused</c>. Null restores the default.
        /// </summary>
        public static Func<bool>? IsVisible { get; set; }

        internal static bool ReadOnline() =>
            IsOnline?.Invoke() ?? Application.internetReachability != NetworkReachability.NotReachable;

        internal static bool ReadVisible() => IsVisible?.Invoke() ?? ApplicationVisibility.IsVisible();
    }
}
