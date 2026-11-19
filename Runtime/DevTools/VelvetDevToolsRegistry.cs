#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace Velvet.DevTools
{
    /// <summary>
    /// Registry of fibers observed by the DevTools window. <see cref="V.Mount"/> registers its root, and a
    /// fiber leaves the registry when it is disposed, whoever registered it.
    /// <para>
    /// Register an interior subtree explicitly when it needs its own label:
    /// <code>
    ///   VelvetDevToolsRegistry.Register(myFiber, "MyPage");
    /// </code>
    /// </para>
    /// It lives in the runtime assembly rather than beside the window in <c>Editor/DevTools/</c> because
    /// <see cref="V.Mount"/> calls it and Velvet.Editor references Velvet, not the other way round.
    /// </summary>
    public static class VelvetDevToolsRegistry
    {
        public sealed class ComponentEntry
        {
            public ComponentFiber Fiber { get; }

            public string Label { get; internal set; }

            public string TypeName { get; }

            public DateTime RegisteredAt { get; } = DateTime.Now;

            internal ComponentEntry(ComponentFiber fiber, string label)
            {
                Fiber = fiber;
                Label = label;
                TypeName = Hooks.ComponentName(fiber);
            }
        }

        private static readonly List<ComponentEntry> s_entries = new();

        public static event Action? RegistryChanged;

        public static IReadOnlyList<ComponentEntry> Entries => s_entries;

        /// <summary>
        /// Adds a fiber, or relabels its existing entry when registered again. A disposed fiber is not added.
        /// </summary>
        /// <param name="fiber">The fiber to observe.</param>
        /// <param name="label">Display name in the EditorWindow. Defaults to the component's name when omitted.</param>
        public static void Register(ComponentFiber fiber, string? label = null)
        {
            if (fiber == null)
            {
                throw new ArgumentNullException(nameof(fiber));
            }

            if (fiber.IsDisposed) return;

            var resolvedLabel = label ?? Hooks.ComponentName(fiber);
            foreach (var entry in s_entries)
            {
                if (ReferenceEquals(entry.Fiber, fiber))
                {
                    entry.Label = resolvedLabel;
                    RegistryChanged?.Invoke();
                    return;
                }
            }

            s_entries.Add(new ComponentEntry(fiber, resolvedLabel));
            RegistryChanged?.Invoke();
        }

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

        public static void Clear()
        {
            s_entries.Clear();
            RegistryChanged?.Invoke();
        }
    }
}
#endif
