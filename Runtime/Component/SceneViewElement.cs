#nullable enable
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// The element behind <see cref="V.SceneView"/>: displays a Camera's output as its background
    /// image, from a framework-owned RenderTexture sized to the element's laid-out rect. A dedicated
    /// subclass (rather than reusing another element type) so a type change to or from any other
    /// element remounts instead of patching, and so the element is never recycled through the shared
    /// primitive pools while it owns a live RenderTexture. While the camera texture is live it owns
    /// the element's <c>backgroundImage</c>: every other writer routes through
    /// <see cref="WriteBackground"/>, which defers the value instead of clobbering the feed and
    /// restores it when the camera releases.
    /// </summary>
    public sealed class SceneViewElement : VisualElement
    {
        internal bool CameraOwnsBackground { get; private set; }
        internal StyleBackground DeferredBackground { get; private set; }

        // Called by the SceneView driver when the camera texture takes the slot: whatever the element
        // was showing (a poster, a baked gradient) becomes the deferred restore target. Idempotent so
        // a texture re-create on resize keeps the original capture.
        internal void BeginCameraOwnership()
        {
            if (CameraOwnsBackground)
            {
                return;
            }
            CameraOwnsBackground = true;
            DeferredBackground = style.backgroundImage;
        }

        // Called by the SceneView driver when the texture is released: the last deferred value (or
        // the pre-camera capture) returns to the live style, and later writers land directly again.
        internal void EndCameraOwnership()
        {
            if (!CameraOwnsBackground)
            {
                return;
            }
            CameraOwnsBackground = false;
            style.backgroundImage = DeferredBackground;
            DeferredBackground = default;
        }

        // The single seam every non-camera background writer goes through (the styles diff, the
        // gradient bake, the bg-[addr:…] resolver): a write while the camera owns the slot lands in
        // the deferred value instead of the live style, so the feed survives ordinary restyles.
        internal static void WriteBackground(VisualElement element, StyleBackground value)
        {
            if (element is SceneViewElement { CameraOwnsBackground: true } sceneView)
            {
                sceneView.DeferredBackground = value;
                return;
            }
            element.style.backgroundImage = value;
        }
    }
}
