using System;

namespace Velvet
{
    /// <summary>
    /// Minimal write-side interface exposing a store's state-update API to collaborators.
    /// <see cref="Store{TState}"/> implements it explicitly.
    /// </summary>
    public interface IStoreWriter<TState>
    {
        /// <summary>
        /// Snapshot of the current state.
        /// </summary>
        TState Current { get; }

        /// <summary>
        /// Updates state (equality-checked).
        /// </summary>
        bool SetState(Func<TState, TState> updater);

        /// <summary>
        /// Updates state unconditionally. Used for cases like Reset where notification must always fire.
        /// </summary>
        void Mutate(Func<TState, TState> reducer);
    }
}
