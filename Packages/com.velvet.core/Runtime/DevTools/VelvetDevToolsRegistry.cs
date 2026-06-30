#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace Velvet.DevTools
{
    /// <summary>
    /// Global registry of fibers observed by the DevTools window. Every <see cref="V.Mount"/> auto-registers
    /// its root here (and unregisters on dispose), so opening <b>Window ▸ Velvet ▸ DevTools Inspector</b>
    /// shows the running app's component tree with no manual setup — the React DevTools "just attaches" model.
    /// <para>
    /// Manual registration stays available for labelling an interior sub-tree (e.g. a specific page fiber):
    /// <code>
    ///   VelvetDevToolsRegistry.Register(myFiber, "MyPage");
    ///   VelvetDevToolsRegistry.Unregister(myFiber);
    /// </code>
    /// </para>
    /// Lives in the runtime assembly (so <see cref="V.Mount"/> can reach it) but is editor-only behaviour,
    /// hence the surrounding <c>#if UNITY_EDITOR</c>: player builds exclude it entirely.
    /// </summary>
    public static class VelvetDevToolsRegistry
    {
        /// <summary>
        /// Entry for a registered fiber.
        /// </summary>
        public sealed class ComponentEntry
        {
            /// <summary>The fiber being observed.</summary>
            public ComponentFiber Fiber { get; }

            /// <summary>Display label (e.g. page name or component type name).</summary>
            public string Label { get; }

            /// <summary>Component function name (taken from Body's MethodInfo and cached to avoid reflection inside the OnGUI loop).</summary>
            public string TypeName { get; }

            /// <summary>Registration timestamp.</summary>
            public DateTime RegisteredAt { get; } = DateTime.Now;

            internal ComponentEntry(ComponentFiber fiber, string label)
            {
                Fiber = fiber;
                Label = label;
                TypeName = fiber.Body?.Method?.Name ?? "[Component]";
            }
        }

        private static readonly List<ComponentEntry> s_entries = new();

        /// <summary>Event raised when a fiber is registered or unregistered.</summary>
        public static event Action RegistryChanged;

        /// <summary>Read-only list of currently registered fiber entries.</summary>
        public static IReadOnlyList<ComponentEntry> Entries => s_entries;

        /// <summary>
        /// Registers a fiber with DevTools.
        /// Re-registering the same fiber overwrites the existing entry (its label is updated), so the call is
        /// idempotent: the same fiber never appears twice.
        /// </summary>
        /// <param name="fiber">The fiber to observe.</param>
        /// <param name="label">Display name in the EditorWindow. Defaults to Body's function name when omitted.</param>
        public static void Register(ComponentFiber fiber, string label = null)
        {
            if (fiber == null)
            {
                throw new ArgumentNullException(nameof(fiber));
            }

            var resolvedLabel = label ?? fiber.Body?.Method?.Name ?? "[Component]";
            // Look up the existing entry and overwrite it.
            for (var i = 0; i < s_entries.Count; i++)
            {
                if (ReferenceEquals(s_entries[i].Fiber, fiber))
                {
                    s_entries[i] = new ComponentEntry(fiber, resolvedLabel);
                    RegistryChanged?.Invoke();
                    return;
                }
            }

            s_entries.Add(new ComponentEntry(fiber, resolvedLabel));
            RegistryChanged?.Invoke();
        }

        /// <summary>
        /// Unregisters a fiber from DevTools.
        /// </summary>
        /// <param name="fiber">The fiber to unregister.</param>
        public static void Unregister(ComponentFiber fiber)
        {
            if (fiber == null)
            {
                return;
            }

            for (var i = s_entries.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(s_entries[i].Fiber, fiber))
                {
                    s_entries.RemoveAt(i);
                    RegistryChanged?.Invoke();
                    return;
                }
            }
        }

        /// <summary>
        /// Clears all entries. Call this on play-mode exit, for example.
        /// </summary>
        public static void Clear()
        {
            s_entries.Clear();
            RegistryChanged?.Invoke();
        }
    }
}
#endif
