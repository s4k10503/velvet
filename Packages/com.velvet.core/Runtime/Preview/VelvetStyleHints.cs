#if UNITY_EDITOR
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// Transiently hands an additional stylesheet from <c>[VelvetPreviewSetup]</c> to the next preview host.
    /// <para>
    /// This static channel supports one active setup-to-host handoff. Concurrent mounts are unsupported because
    /// the first host consumes and clears the value.
    /// </para>
    /// </summary>
    public static class VelvetStyleHints
    {
        /// <summary>
        /// An additional stylesheet the next preview-host mount attaches to the canvas. Where it lands
        /// among the sheets already there is decided by whoever attaches them, not here.
        /// </summary>
        public static StyleSheet? PreviewStyleSheet { get; set; }
    }
}
#endif
