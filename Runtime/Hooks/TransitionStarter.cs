#nullable enable
using System;
using Cysharp.Threading.Tasks;

namespace Velvet
{
    /// <summary>
    /// The <c>startTransition</c> function returned by <see cref="Hooks.UseTransition"/>. Accepts
    /// either a synchronous callback (<c>startTransition(() =&gt; ...)</c>) or
    /// an async one (<c>startTransition(async () =&gt; ...)</c>). State updates inside the callback are scheduled
    /// on the Transition lane; for the async form, <c>isPending</c> stays true across awaits until the task
    /// completes. Nested calls join the outer transition.
    /// </summary>
    /// <remarks>
    /// A struct (no allocation) wrapping the two cached closures built once per render slot. Reference-stable, so
    /// placing a <see cref="TransitionStarter"/> in a dependency array does not spuriously change it.
    /// </remarks>
    public readonly struct TransitionStarter : IEquatable<TransitionStarter>
    {
        private readonly Action<Action> _start;
        private readonly Action<Func<UniTask>> _startAsync;

        internal TransitionStarter(Action<Action> start, Action<Func<UniTask>> startAsync)
        {
            _start = start;
            _startAsync = startAsync;
        }

        /// <summary>Runs <paramref name="updates"/> at Transition priority.</summary>
        /// <param name="updates">Synchronous callback whose state updates run at Transition priority.</param>
        public void Invoke(Action updates) => _start?.Invoke(updates);

        /// <summary>
        /// Runs an async <paramref name="asyncUpdates"/> at Transition priority, keeping <c>isPending</c> true
        /// across awaits until it completes.
        /// </summary>
        /// <param name="asyncUpdates">Async callback whose state updates run at Transition priority.</param>
        public void Invoke(Func<UniTask> asyncUpdates) => _startAsync?.Invoke(asyncUpdates);

        /// <summary>
        /// Implicit conversion to <see cref="Action{Action}"/> so the starter can be stored in / passed as an
        /// <c>Action&lt;Action&gt;</c> (the synchronous form). The returned delegate is the cached starter.
        /// </summary>
        /// <param name="starter">The starter to convert.</param>
        public static implicit operator Action<Action>(TransitionStarter starter) => starter._start;

        /// <summary>Value equality over the two wrapped (reference-stable) delegates.</summary>
        /// <param name="other">The other starter to compare.</param>
        public bool Equals(TransitionStarter other)
            => Equals(_start, other._start) && Equals(_startAsync, other._startAsync);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is TransitionStarter other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
            => (_start?.GetHashCode() ?? 0) ^ (_startAsync?.GetHashCode() ?? 0);
    }
}
