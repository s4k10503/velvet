#nullable enable
using System;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// When a Particles element starts its effect.
    /// </summary>
    public enum PlayTrigger
    {
        /// <summary>Play as soon as the element mounts (and whenever the effect instance is recreated).</summary>
        Mount,
        /// <summary>Never auto-play; the effect is instantiated stopped, for imperative control.</summary>
        Manual,
    }

    /// <summary>
    /// The element behind <see cref="V.Particles"/>: draws a hidden ParticleSystem simulation as
    /// textured quads in its own visual content. A dedicated subclass so a type change to or from any
    /// other element remounts instead of patching, and so the element is never recycled through the
    /// shared primitive pools while it owns a live simulation host.
    /// </summary>
    public sealed class ParticlesElement : VisualElement
    {
        // Assigned by the driver while a binding is attached; null otherwise. Routed through
        // delegates (rather than the element holding the host) so the element type carries no
        // simulation state of its own.
        internal Action? PlayHandler;
        internal Action? StopHandler;

        /// <summary>
        /// Starts the bound effect — the imperative half of <see cref="PlayTrigger.Manual"/> (a
        /// <see cref="PlayTrigger.Mount"/> element may also use it to replay a finished burst).
        /// A no-op while no effect is bound.
        /// </summary>
        public void Play() => PlayHandler?.Invoke();

        /// <summary>
        /// Stops the bound effect and clears its live particles. A no-op while no effect is bound.
        /// </summary>
        public void Stop() => StopHandler?.Invoke();
    }
}
