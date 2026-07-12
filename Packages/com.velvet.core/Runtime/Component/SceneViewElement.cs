#nullable enable
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// The element behind <see cref="V.SceneView"/>: displays a Camera's output as its background
    /// image, from a framework-owned RenderTexture sized to the element's laid-out rect. A dedicated
    /// subclass (rather than reusing another element type) so a type change to or from any other
    /// element remounts instead of patching, and so the element is never recycled through the shared
    /// primitive pools while it owns a live RenderTexture.
    /// </summary>
    public sealed class SceneViewElement : VisualElement
    {
    }
}
