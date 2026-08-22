#nullable enable
using System;

namespace Velvet
{
    /// <summary>
    /// The <c>startTransition</c> function returned by <see cref="Hooks.UseTransition"/>. Accepts
    /// either a synchronous callback (<c>startTransition(() =&gt; ...)</c>) or
    /// an async one (<c>startTransition(async () =&gt; ...)</c>). The state updates the callback schedules
    /// before it first suspends are put on the Transition lane, whichever component owns the state they
    /// write; for the async form, <c>isPending</c> stays true across awaits until the task completes, while
    /// the updates the action makes after an <c>await</c> that suspended it fall outside the scope this call
    /// opened — wrap them in a further <c>startTransition</c> to put them back in it. An <c>await</c> of a
    /// task that had already completed does not suspend, so what follows it is still inside the scope; the
    /// migration guide's <c>useTransition</c> row owns that rule. Nested calls join the outer transition.
    /// </summary>
    /// <remarks>
    /// A struct (no allocation) wrapping the two cached closures built once per render slot. Reference-stable, so
    /// placing a <see cref="TransitionStarter"/> in a dependency array does not spuriously change it.
    /// </remarks>
    public readonly struct TransitionStarter : IEquatable<TransitionStarter>
    {
        private readonly Action<Action> _start;
        private readonly Action<Func<VelvetTask>> _startAsync;

        internal TransitionStarter(Action<Action> start, Action<Func<VelvetTask>> startAsync)
        {
            _start = start;
            _startAsync = startAsync;
        }

        /// <summary>Runs <paramref name="updates"/> at Transition priority.</summary>
        /// <param name="updates">Synchronous callback whose state updates run at Transition priority.</param>
        public void Invoke(Action updates) => _start?.Invoke(updates);

        /// <summary>
        /// Runs <paramref name="asyncUpdates"/> at Transition priority up to the point it first suspends,
        /// keeping <c>isPending</c> true across awaits until it completes. The updates it makes past a
        /// suspension need a further <c>startTransition</c> to be part of the transition.
        /// </summary>
        /// <param name="asyncUpdates">Async callback whose state updates run at Transition priority.</param>
        public void Invoke(Func<VelvetTask> asyncUpdates) => _startAsync?.Invoke(asyncUpdates);

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
