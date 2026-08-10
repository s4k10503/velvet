using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// Registry that registers and retrieves the destination VisualElement for a Portal.
    /// A Portal renders its children into the container looked up here by id, rather than into its
    /// own position in the tree.
    /// Not thread-safe (main thread only).
    /// </summary>
    public static class FiberPortalRegistry
    {
        private static readonly Dictionary<string, VisualElement> _targets = new();

        // Raised on every accepted registration. A live Portal holds the element its children mounted
        // into and no render reads this table again, so nothing observes a swap without this signal.
        // Whether a given registration IS a swap is left to the subscriber (ReconcilerContext) rather
        // than tested here against the previous entry: an id unregistered by a departing screen and
        // registered by its replacement leaves nothing here to compare against, while a Portal still
        // holds the element it mounted into.
        internal static event System.Action<string, VisualElement>? TargetRegistered;

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticFields() => _targets.Clear();
#endif

        /// <summary>
        /// Registers a Portal mount target.
        /// A second registration of the same ID logs a warning and overwrites.
        /// </summary>
        /// <param name="id">Identifier used by Portal consumers to look up the mount target. Null or empty values are rejected with a warning.</param>
        /// <param name="target">The destination <see cref="VisualElement"/> that Portal children are appended to. Null is rejected with a warning.</param>
        public static void Register(string id, VisualElement target)
        {
            if (string.IsNullOrEmpty(id))
            {
                Debug.LogWarning("[FiberPortalRegistry] Cannot register with null or empty id.");
                return;
            }

            if (target == null)
            {
                Debug.LogWarning($"[FiberPortalRegistry] Cannot register null target for id \"{id}\".");
                return;
            }

            if (NameKeyedRegistry.Set(id, target, _targets))
            {
                Debug.LogWarning($"[FiberPortalRegistry] Id \"{id}\" is already registered. Overwriting.");
            }
            // After the table is written, so a subscriber that re-resolves the id reads this target.
            TargetRegistered?.Invoke(id, target);
        }

        /// <summary>
        /// Unregisters a Portal mount target.
        /// </summary>
        /// <param name="id">Identifier previously passed to <see cref="Register"/>. Null or empty values are no-ops.</param>
        public static void Unregister(string id) => NameKeyedRegistry.Unregister(id, _targets);

        /// <summary>
        /// Gets a Portal mount target. Returns null if not registered.
        /// </summary>
        /// <param name="id">Identifier previously passed to <see cref="Register"/>.</param>
        /// <returns>The registered <see cref="VisualElement"/>, or <c>null</c> when <paramref name="id"/> is null/empty or unregistered.</returns>
        public static VisualElement? Get(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            return _targets.GetValueOrDefault(id);
        }

        /// <summary>
        /// Returns whether the ID is registered.
        /// </summary>
        /// <param name="id">Identifier to test. Null or empty values always return <c>false</c>.</param>
        /// <returns><c>true</c> when a target is currently registered for <paramref name="id"/>; otherwise <c>false</c>.</returns>
        public static bool IsRegistered(string id) => NameKeyedRegistry.IsRegistered(id, _targets);
    }
}
