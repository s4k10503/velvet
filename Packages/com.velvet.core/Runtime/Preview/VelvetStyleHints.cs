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
        /// An additional stylesheet consumed by the next preview-host mount. The host clears the handoff, adds
        /// the sheet when the target does not already own it, and removes only the sheet it added.
        /// </summary>
        public static StyleSheet? PreviewStyleSheet { get; set; }
    }
}
#endif
