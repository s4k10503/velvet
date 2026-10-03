#nullable enable
using UnityEngine.UIElements;

namespace Velvet
{
    // What a holder of a layoutId looked like where its box was read: the values Framer's mixValues mixes a shared
    // layout animation's lead in from (mix-values.ts), which NodeStack.promote takes as the previous lead's
    // animationValues or else its latestValues (stack.ts).
    internal readonly struct LayoutIdLook
    {
        // The source of a look whose holder has let go of the id: an element no Motion is, so no id holds it.
        private static readonly VisualElement s_released = new();

        private LayoutIdLook(float opacity, VisualElement source)
        {
            Opacity = opacity;
            Source = source;
        }

        public float Opacity { get; }
        // The holder whose own opacity is read again while it still holds the id; compared by reference only, since
        // once it leaves the pool can hand it to another Motion.
        public VisualElement Source { get; }

        // Without the holder, which has let go of the id: its element can come back from the pool as another Motion.
        public LayoutIdLook Released() => new(Opacity, s_released);

        // A holder read mid-way through a shared move of its own gives the opacity that move drew it at, and no
        // holder to read again, as promote takes animationValues, which the new lead's animation stops.
        public static LayoutIdLook Read(VisualElement element, LayoutIdProjection? projection) =>
            projection is { Shared: true, Moving: true }
                ? new LayoutIdLook(element.resolvedStyle.opacity, s_released)
                : new LayoutIdLook(MotionOpacity.Own(element), element);
    }
}
