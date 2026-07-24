#nullable enable
using System;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// Reference holder for an element or imperative handle.
    /// Receives a VisualElement-derived type via <c>refCallback:</c>, and can also hold any handle
    /// interface exposed by <c>UseImperativeHandle</c>.
    /// </summary>
    /// <typeparam name="T">Type being referenced. Only constrained to class; not restricted to VisualElement.</typeparam>
    public sealed class Ref<T> : IHookRefSetter where T : class
    {
        // Both delegates are allocated once per Ref so their identities are stable for the Ref's
        // lifetime: the cleanup always performs the same action (Set(null)), and the setter itself
        // must be ONE delegate instance — a method group (`refCallback: _ref.SetElement`) would
        // convert to a fresh delegate every render, so the reconciler's ref-identity gate (a ref
        // cycles only when its identity changes) could never recognize the same Ref across patches
        // and would cycle it per render — a C# delegate-conversion detail breaking object-ref stability.
        private readonly Action _clearAction;
        private readonly Func<VisualElement, Action> _setElement;

        /// <summary>Creates an empty ref whose <see cref="Current"/> is null until assigned.</summary>
        public Ref()
        {
            _clearAction = () => ((IHookRefSetter)this).Set(null);
            _setElement = element =>
            {
                ((IHookRefSetter)this).Set(element);
                return _clearAction;
            };
        }

        /// <summary>Referenced target. null before mount and after unmount. Can be overwritten at any time via <see cref="Set(T?)"/>.</summary>
        public T? Current { get; private set; }

        /// <summary>
        /// Directly overwrites the referenced target.
        /// Pass <c>Set(null)</c> to clear the reference (for example during cleanup).
        /// </summary>
        public void Set(T? value) => Current = value;

        /// <summary>
        /// Callback ref setter. Used as <c>V.Button(refCallback: _ref.SetElement)</c>.
        /// Stores the element into <see cref="Current"/>; the returned <c>Action</c> is the cleanup that
        /// resets <c>Current = null</c> on detach. The same delegate instance is returned for the
        /// Ref's lifetime, so a patch that passes it again leaves the installed ref untouched.
        /// For T values not derived from VisualElement, <c>element as T</c> is always null, so this is
        /// primarily used for Element-node refs.
        /// </summary>
        public Func<VisualElement, Action> SetElement => _setElement;

        void IHookRefSetter.Set(object? value) => Current = value as T;

        object? IHookRefSetter.RecycleMarkRoot => HookSlotRecycleProbe.Probe(Current);
    }
}
