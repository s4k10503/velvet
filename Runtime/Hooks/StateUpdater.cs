#nullable enable
using System;

namespace Velvet
{
    /// <summary>
    /// The single state setter returned by <see cref="Hooks.UseState{T}(T)"/>. Accepts either a
    /// replacement value (<c>setValue(next)</c>) or a functional
    /// updater (<c>setValue(prev =&gt; next)</c>). The functional form always reads the latest committed
    /// value, so it is safe to invoke from a closure captured by an earlier render (no stale-closure pitfall).
    /// </summary>
    /// <remarks>
    /// A struct (no allocation) wrapping the two cached closures built once at slot creation. The wrapped
    /// closures are reference-stable across renders, so a captured <see cref="StateUpdater{T}"/> stays valid.
    /// Equal-value updates do not request a re-render (identity-based bailout).
    /// </remarks>
    /// <typeparam name="T">State type.</typeparam>
    public readonly struct StateUpdater<T> : IEquatable<StateUpdater<T>>
    {
        private readonly Action<T> _setValue;
        private readonly Action<Func<T, T>> _updateValue;

        internal StateUpdater(Action<T> setValue, Action<Func<T, T>> updateValue)
        {
            _setValue = setValue;
            _updateValue = updateValue;
        }

        /// <summary>Replaces the state with <paramref name="next"/>.</summary>
        /// <param name="next">The new state value.</param>
        public void Invoke(T next) => _setValue?.Invoke(next);

        /// <summary>
        /// Computes the next state from the latest committed value (<c>setValue(prev =&gt; next)</c>).
        /// Reads the current value at invocation time, so it is safe to call from a stale closure.
        /// </summary>
        /// <param name="updater">Function receiving the latest value and returning the next value. Must not be null.</param>
        public void Invoke(Func<T, T> updater) => _updateValue?.Invoke(updater);

        /// <summary>
        /// Implicit conversion to the direct value-setter <see cref="Action{T}"/> so the setter can be stored
        /// in / passed as an <c>Action&lt;T&gt;</c> (e.g. a callback parameter) and invoked with value-call syntax.
        /// The returned delegate is the cached, reference-stable value-setter.
        /// </summary>
        /// <param name="updater">The setter to convert.</param>
        public static implicit operator Action<T>(StateUpdater<T> updater) => updater._setValue;

        /// <summary>
        /// Implicit conversion to the functional-updater <see cref="Action{T}"/> of <c>Func&lt;T, T&gt;</c>, for
        /// callers that want to pass the <c>prev =&gt; next</c> form as a delegate.
        /// </summary>
        /// <param name="updater">The setter to convert.</param>
        public static implicit operator Action<Func<T, T>>(StateUpdater<T> updater) => updater._updateValue;

        /// <summary>
        /// Value equality over the two wrapped (reference-stable) delegates. Two setters for the same state
        /// slot are equal across renders, so a <see cref="StateUpdater{T}"/> placed in a dependency array
        /// stays stable (UseCallback / UseEffect deps do not spuriously change).
        /// </summary>
        /// <param name="other">The other setter to compare.</param>
        public bool Equals(StateUpdater<T> other)
            => Equals(_setValue, other._setValue) && Equals(_updateValue, other._updateValue);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is StateUpdater<T> other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
            => (_setValue?.GetHashCode() ?? 0) ^ (_updateValue?.GetHashCode() ?? 0);
    }
}
