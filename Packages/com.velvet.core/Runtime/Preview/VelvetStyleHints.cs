#if UNITY_EDITOR
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// A transient handoff for a <c>[VelvetPreviewSetup]</c> to give the preview host an extra stylesheet to
    /// attach to the mount canvas. A setup runs as global code with no reference to the canvas it will render
    /// onto, yet some apps need a project stylesheet (design-token <c>:root</c> overrides, a custom theme)
    /// layered on top of Velvet's utilities for the preview to match the running app. The setup publishes the
    /// sheet here; the host consumes it (clearing this back to <c>null</c>) on the very next mount.
    /// <para>
    /// This is a single-active-host channel: it carries a value only across the brief window between a setup
    /// running and the next <see cref="VelvetPreviewHost"/> mount consuming it. It is not a persistent global —
    /// the host nulls it the moment it reads it, and a well-behaved setup also nulls it on teardown — so two
    /// hosts mounting concurrently is unsupported (the editor drives one preview/capture host at a time).
    /// </para>
    /// <para>
    /// Deferred: a fuller design would thread the extra stylesheet through the setup's return value (so it never
    /// lives in a static at all). That is out of scope here; until then the static channel above is the contract,
    /// kept safe by the host-consumes + setup-clears symmetry.
    /// </para>
    /// </summary>
    public static class VelvetStyleHints
    {
        /// <summary>
        /// An additional stylesheet the next preview-host mount attaches to the canvas after Velvet's utility
        /// sheet (so its later source order lets equal-specificity <c>:root</c> overrides win). The host clears
        /// this to <c>null</c> as it consumes it; a setup that needs no extra sheet simply leaves it <c>null</c>.
        /// </summary>
        public static StyleSheet PreviewStyleSheet { get; set; }
    }
}
#endif
