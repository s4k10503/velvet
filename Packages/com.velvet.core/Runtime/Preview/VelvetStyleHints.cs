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
        /// An additional stylesheet consumed by the next preview-host mount. It is attached after Velvet's
        /// utility sheet, which the caller puts on the canvas first, so the later source order lets an
        /// equal-specificity <c>:root</c> override in this sheet win — which is what the channel is for.
        /// </summary>
        public static StyleSheet? PreviewStyleSheet { get; set; }
    }
}
#endif
