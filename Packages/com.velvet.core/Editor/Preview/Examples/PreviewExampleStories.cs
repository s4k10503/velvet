#if UNITY_EDITOR
using System.Collections.Generic;

namespace Velvet.Editor.Preview
{
    /// <summary>
    /// Verification <c>[VelvetPreview]</c> stories used while developing Velvet's own Preview window: they give
    /// this repo's otherwise-empty story list something to render so the window's viewport / centering /
    /// zoom-and-scroll behavior can be eyeballed live. They are developer-only — the package publish strips this
    /// folder, so an installed Velvet never injects these <c>Examples/*</c> entries into a consumer's Preview
    /// window; they exist only in this development project. Each is also a minimal example of authoring a
    /// <c>[VelvetPreview]</c> story: a static, parameterless method returning a <see cref="VNode"/>, discovered
    /// automatically with no further registration.
    /// </summary>
    internal static class PreviewExampleStories
    {
        private const int TallListRowCount = 30;

        /// <summary>
        /// No explicit Width/Height, so the canvas fills the stage (the Full viewport) or a simulated reference
        /// size (a custom viewport), becoming a responsive scope in the latter case. Background color and flex
        /// direction shift at the sm/md/lg breakpoints, and one caption only shows below the sm breakpoint —
        /// editing the toolbar's W field (or switching between Full and a custom size) makes the shift visible
        /// immediately.
        /// </summary>
        [VelvetPreview(Group = "Examples", Name = "Responsive")]
        private static VNode Responsive()
        {
            return V.Div(
                "flex-col sm:flex-row items-center justify-center gap-4 p-6 w-full h-full " +
                "bg-danger sm:bg-highlight md:bg-primary-soft lg:bg-success",
                V.Label(
                    className: "text-white font-bold text-center",
                    text: "Resize the viewport width to see sm/md/lg flip"),
                V.Label(
                    className: "text-white text-center",
                    text: "sm 640px: row layout + highlight background. md 768px: primary background. " +
                        "lg 1024px: success background."),
                V.Label(
                    className: "sm:hidden text-white text-center",
                    text: "Below 640px only - this note disappears once sm: takes over."));
        }

        /// <summary>
        /// Explicit Width/Height: the canvas is sized to exactly 320x200 regardless of the stage or viewport, and
        /// is NOT a responsive scope. At this footprint the story is smaller than the stage, so mounting it also
        /// confirms the stage centers a small explicit-size story (rather than pinning it to a corner) and that
        /// it scales with the zoom toolbar.
        /// </summary>
        [VelvetPreview(Group = "Examples", Name = "Card", Width = 320, Height = 200)]
        private static VNode Card()
        {
            return V.Div(
                "w-full h-full flex-col justify-center gap-2 p-4 bg-surface border border-default rounded-2xl",
                V.Label(className: "text-lg font-bold text-strong", text: "Fixed-size Card"),
                V.Label(
                    className: "text-sm text-subtle",
                    text: "Authored at 320x200. Stays centered in the stage and scales with zoom."));
        }

        /// <summary>
        /// An explicit-size story taller than any reasonable stage (360x1400). Each row carries <c>shrink-0</c>
        /// so the flex column never squeezes them to fit — at 100%/200% zoom or a short window the stage's
        /// ScrollView must pan to reach the lower rows, and the Outline/Measure overlay should keep tracking a
        /// row while it scrolls.
        /// </summary>
        [VelvetPreview(Group = "Examples", Name = "Tall List", Width = 360, Height = 1400)]
        private static VNode TallList()
        {
            var rows = new List<int>(TallListRowCount);
            for (var i = 0; i < TallListRowCount; i++) rows.Add(i);

            return V.Div(
                "flex-col w-full h-full bg-neutral",
                V.List(rows, i => $"row-{i}", i => V.Div(
                    "shrink-0 flex-row items-center justify-between p-3 border-b border-default bg-surface",
                    V.Label(className: "text-sm text-strong", text: $"Row {i + 1}"),
                    V.Label(className: "text-xs text-subtle", text: "flexShrink: 0"))));
        }
    }
}
#endif
