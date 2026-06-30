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
        // The SetElement cleanup always performs the same action (Set(null)) for a given Ref<T> instance,
        // so allocate it once in the ctor to avoid per-mount closure allocation.
        private readonly Action _clearAction;

        /// <summary>Creates an empty ref whose <see cref="Current"/> is null until assigned.</summary>
        public Ref()
        {
            _clearAction = () => ((IHookRefSetter)this).Set(null);
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
        /// resets <c>Current = null</c> on detach.
        /// For T values not derived from VisualElement, <c>element as T</c> is always null, so this is
        /// primarily used for Element-node refs.
        /// </summary>
        public Action SetElement(VisualElement element)
        {
            ((IHookRefSetter)this).Set(element);
            return _clearAction;
        }

        void IHookRefSetter.Set(object? value) => Current = value as T;
    }
}
