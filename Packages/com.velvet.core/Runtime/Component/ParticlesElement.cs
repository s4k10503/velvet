#nullable enable
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
    }
}
