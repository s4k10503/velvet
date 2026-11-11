using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// An ordered record of what a tree's components did — rendered, set up or cleaned up an insertion, layout or
    /// passive effect, attached or detached a ref — read back as one space-separated string, so a case asserts a
    /// whole commit sequence with a single comparison.
    /// </summary>
    /// <remarks>
    /// A tree reaches its log through <see cref="Provide"/> at the root rather than through a static, so no
    /// fixture's entries reach another's. The probes never read the host tree, the element a ref callback is
    /// handed included, so that a case moves to another renderer by its mount line alone.
    /// </remarks>
    public sealed class EventLog
    {
        private static readonly ComponentContext<EventLog?> s_context = ComponentContext<EventLog?>.Create(null);

        private readonly List<string> _entries = new();
        private readonly Dictionary<string, object> _setters = new();

        /// <summary>Appends one entry.</summary>
        public void Add(string entry) => _entries.Add(entry);

        /// <summary>Drops every entry, keeping the setters <see cref="UseState{T}"/> registered.</summary>
        public void Clear() => _entries.Clear();

        /// <summary>The entries in order, separated by single spaces.</summary>
        public override string ToString() => string.Join(" ", _entries);

        /// <summary>Wraps <paramref name="root"/> so every probe under it records into this log.</summary>
        public VNode Provide(VNode root) => V.Provider(s_context, this, new[] { root });

        /// <summary>The setter the component that called <see cref="UseState{T}"/> under <paramref name="name"/> last rendered with.</summary>
        public StateUpdater<T> Setter<T>(string name)
            => _setters.TryGetValue(name, out var setter)
                ? (StateUpdater<T>)setter
                : throw new KeyNotFoundException($"No component has rendered a state probe named '{name}'");

        /// <summary>The log <see cref="Provide"/> put above the calling component.</summary>
        public static EventLog UseLog()
            => Hooks.UseContext(s_context)
                ?? throw new InvalidOperationException("A probe rendered outside EventLog.Provide");

        /// <summary>Records <c>render:<paramref name="name"/></c> for this render.</summary>
        public static void UseRender(string name) => UseLog().Add("render:" + name);

        /// <summary>
        /// Registers an insertion, a layout and a passive effect over <paramref name="deps"/>, recording
        /// <c>ins:</c>, <c>lay:</c> and <c>eff:</c> on setup and <c>ins-x:</c>, <c>lay-x:</c> and <c>eff-x:</c> on
        /// cleanup, each followed by <paramref name="name"/>.
        /// </summary>
        public static void UseEffects(string name, object?[]? deps)
        {
            var log = UseLog();
            Hooks.UseInsertionEffect(() => Record(log, "ins", name), deps);
            Hooks.UseLayoutEffect(() => Record(log, "lay", name), deps);
            Hooks.UseEffect(() => Record(log, "eff", name), deps);
        }

        /// <summary>
        /// A ref callback, the same instance on every render, recording <c>ref:<paramref name="name"/></c> on attach
        /// and <c>ref-x:<paramref name="name"/></c> when the cleanup it returns runs.
        /// </summary>
        public static Func<VisualElement, Action> UseRefCallback(string name)
        {
            var log = UseLog();
            return Hooks.UseCallback<Func<VisualElement, Action>>(
                _ => Record(log, "ref", name), Array.Empty<object>());
        }

        /// <summary>
        /// <see cref="Hooks.UseState{T}(T)"/>, with its setter published under <paramref name="name"/> for
        /// <see cref="Setter{T}"/>, so a case drives the update the way an application does.
        /// </summary>
        public static (T value, StateUpdater<T> setValue) UseState<T>(string name, T initial)
        {
            var log = UseLog();
            var (value, setValue) = Hooks.UseState(initial);
            log._setters[name] = setValue;
            return (value, setValue);
        }

        private static Action Record(EventLog log, string kind, string name)
        {
            log.Add(kind + ":" + name);
            return () => log.Add(kind + "-x:" + name);
        }
    }
}
