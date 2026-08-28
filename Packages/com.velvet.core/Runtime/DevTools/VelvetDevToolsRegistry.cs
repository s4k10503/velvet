#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace Velvet.DevTools
{
    /// <summary>
    /// Registry of fibers observed by the DevTools window. <see cref="V.Mount"/> registers its root, and
    /// disposing the mounted tree unregisters it.
    /// <para>
    /// Register and unregister an interior subtree explicitly when it needs its own label:
    /// <code>
    ///   VelvetDevToolsRegistry.Register(myFiber, "MyPage");
    ///   VelvetDevToolsRegistry.Unregister(myFiber);
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

            public string Label { get; }

            public string TypeName { get; }

            public DateTime RegisteredAt { get; } = DateTime.Now;

            internal ComponentEntry(ComponentFiber fiber, string label)
            {
                Fiber = fiber;
                Label = label;
                TypeName = fiber.Body?.Method?.Name ?? "[Component]";
            }
        }

        private static readonly List<ComponentEntry> s_entries = new();

        public static event Action? RegistryChanged;

        public static IReadOnlyList<ComponentEntry> Entries => s_entries;

        /// <summary>
        /// Adds a fiber, or replaces its existing entry when registered again.
        /// </summary>
        /// <param name="fiber">The fiber to observe.</param>
        /// <param name="label">Display name in the EditorWindow. Defaults to Body's function name when omitted.</param>
        public static void Register(ComponentFiber fiber, string? label = null)
        {
            if (fiber == null)
            {
                throw new ArgumentNullException(nameof(fiber));
            }

            var resolvedLabel = label ?? fiber.Body?.Method?.Name ?? "[Component]";
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
