using System;
using System.Collections.Generic;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// An ordered record of what a tree's components did — rendered, read a context, set up or cleaned up an
    /// insertion, layout or passive effect, attached or detached a ref, committed a value — and of the errors the root
    /// reported, read back as one space-separated string, so a case asserts a whole commit sequence with a single
    /// comparison.
    /// </summary>
    /// <remarks>
    /// A tree reaches its log through <see cref="Provide"/> at the root rather than through a static, so no
    /// fixture's entries reach another's. The probes never read the host tree, the instance a ref callback is
    /// handed included.
    /// </remarks>
    public sealed class EventLog
    {
        private static readonly ComponentContext<EventLog?> s_context = ComponentContext<EventLog?>.Create(null);

        private readonly List<string> _entries = new();
        private readonly Dictionary<string, object> _setters = new();
        private readonly Dictionary<string, TransitionStarter> _transitions = new();

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

        /// <summary>Records <c>render:<paramref name="name"/>=<paramref name="value"/></c> for this render.</summary>
        public static void UseRender(string name, object? value) => UseLog().Add("render:" + name + "=" + value);

        /// <summary>
        /// <see cref="Hooks.UseContext{T}"/>, recording <c>ctx:<paramref name="name"/>=</c> and the value it read
        /// for this render.
        /// </summary>
        public static T? UseContext<T>(string name, ComponentContext<T> context)
        {
            var log = UseLog();
            var value = Hooks.UseContext(context);
            log.Add("ctx:" + name + "=" + value);
            return value;
        }

        /// <summary>
        /// Records <c>commit:<paramref name="name"/>=<paramref name="value"/></c> from a layout effect keyed on
        /// <paramref name="value"/>, so a case reads what the commits showed rather than what each render saw: once
        /// for the commit that mounts the component, once for each commit that changes the value, and once wherever
        /// the component's layout effects are set up again.
        /// </summary>
        public static void UseCommit(string name, object? value)
        {
            var log = UseLog();
            Hooks.UseLayoutEffect(() =>
            {
                log.Add("commit:" + name + "=" + value);
                return (Action?)null;
            }, new[] { value });
        }

        /// <summary>
        /// <see cref="Hooks.UseTransition"/>, with its starter published under <paramref name="name"/> for
        /// <see cref="Transition"/>, so a case starts the transition the way an application does.
        /// </summary>
        public static bool UseTransition(string name)
        {
            var log = UseLog();
            var (isPending, start) = Hooks.UseTransition();
            log._transitions[name] = start;
            return isPending;
        }

        /// <summary>The starter the component that called <see cref="UseTransition"/> under <paramref name="name"/> last rendered with.</summary>
        public TransitionStarter Transition(string name)
            => _transitions.TryGetValue(name, out var start)
                ? start
                : throw new KeyNotFoundException($"No component has rendered a transition probe named '{name}'");

        /// <summary>
        /// Mount options whose <see cref="MountOptions.OnCaughtError"/> records <c>caught:</c> followed by the
        /// boundary's name, a colon and the error's message, and whose <see cref="MountOptions.OnUncaughtError"/>
        /// records <c>uncaught:</c> followed by the message.
        /// </summary>
        public MountOptions ReportingErrors() => new()
        {
            OnCaughtError = (error, info) => Add("caught:" + info.ErrorBoundary + ":" + error.Message),
            OnUncaughtError = (error, _) => Add("uncaught:" + error.Message),
        };

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
        /// <see cref="UseEffects"/> without its passive effect: records <c>ins:</c> and <c>lay:</c> on setup and
        /// <c>ins-x:</c> and <c>lay-x:</c> on cleanup, each followed by <paramref name="name"/>.
        /// </summary>
        public static void UseInsertionAndLayoutEffects(string name, object?[]? deps)
        {
            var log = UseLog();
            Hooks.UseInsertionEffect(() => Record(log, "ins", name), deps);
            Hooks.UseLayoutEffect(() => Record(log, "lay", name), deps);
        }

        /// <summary>
        /// A ref callback, the same instance on every render, recording <c>ref:<paramref name="name"/></c> on attach
        /// and <c>ref-x:<paramref name="name"/></c> when the cleanup it returns runs.
        /// </summary>
        /// <typeparam name="TInstance">The host instance the renderer hands a ref callback.</typeparam>
        public static Func<TInstance, Action> UseRefCallback<TInstance>(string name)
        {
            var log = UseLog();
            return Hooks.UseCallback<Func<TInstance, Action>>(
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
