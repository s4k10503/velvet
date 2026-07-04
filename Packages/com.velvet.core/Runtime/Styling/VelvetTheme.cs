using System;

namespace Velvet
{
    /// <summary>
    /// Global theme state backing the <c>dark:</c> utility variant. This models a
    /// <c>class</c>-based dark-mode strategy as a single application-wide flag rather than scanning for an
    /// ancestor <c>.dark</c> class (UI Toolkit has no class-change event to react to cheaply).
    /// <para/>
    /// Set <see cref="IsDark"/> from your app (e.g. on a settings toggle); every mounted element with
    /// a <c>dark:</c> variant re-evaluates its payload when the value changes.
    /// </summary>
    /// <remarks>Equivalent to Tailwind CSS's class-based dark-mode strategy for users migrating from Tailwind.</remarks>
    public static class VelvetTheme
    {
        private static bool _isDark;

        /// <summary>Raised whenever <see cref="IsDark"/> changes.</summary>
        public static event Action? DarkModeChanged;

        /// <summary>Whether dark mode is active. Setting it notifies <see cref="DarkModeChanged"/>.</summary>
        public static bool IsDark
        {
            get => _isDark;
            set
            {
                if (_isDark == value)
                {
                    return;
                }

                _isDark = value;
                DarkModeChanged?.Invoke();
            }
        }
    }
}
