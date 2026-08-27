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
        /// An additional stylesheet consumed by the next preview-host mount, attached in the order the
        /// mount reaches it. An equal-specificity <c>:root</c> override here wins over one in a sheet
        /// attached earlier and loses to one attached later, which is what a caller publishing design
        /// tokens through this channel is choosing between.
        /// </summary>
        public static StyleSheet? PreviewStyleSheet { get; set; }
    }
}
#endif
