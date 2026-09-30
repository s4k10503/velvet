#nullable enable
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // What a holder of a layoutId looked like where its box was read: the values Framer's mixValues mixes a shared
    // layout animation's lead in from (mix-values.ts), which NodeStack.promote takes as the previous lead's
    // animationValues or else its latestValues (stack.ts). Opacity is the holder's own; rotate and radii are as the
    // holder was drawn.
    internal readonly struct LayoutIdLook
    {
        // The source of a look whose holder has let go of the id: an element no Motion is, so no id holds it.
        private static readonly VisualElement s_released = new();

        private LayoutIdLook(float opacity, float rotate, Vector4 radii, VisualElement source)
        {
            Opacity = opacity;
            Rotate = rotate;
            Radii = radii;
            Source = source;
        }

        public float Opacity { get; }
        // In degrees.
        public float Rotate { get; }
        // Top-left, top-right, bottom-right and bottom-left, in pixels.
        public Vector4 Radii { get; }
        // The holder whose own opacity is read again while it still holds the id; compared by reference only, since
        // once it leaves the pool can hand it to another Motion.
        public VisualElement Source { get; }

        // Without the holder, which has let go of the id: its element can come back from the pool as another Motion.
        public LayoutIdLook Released() => new(Opacity, Rotate, Radii, s_released);

        // A holder read mid-way through a shared move of its own gives the opacity that move drew it at, and no
        // holder to read again, as promote takes animationValues, which the new lead's animation stops.
        public static LayoutIdLook Read(VisualElement element, LayoutIdProjection? projection) =>
            projection is { Shared: true, Moving: true }
                ? new LayoutIdLook(element.resolvedStyle.opacity, RotateOf(element, projection), RadiiOf(element, projection), s_released)
                : new LayoutIdLook(MotionOpacity.Own(element), RotateOf(element, projection), RadiiOf(element, projection), element);

        // The rotate and radii an element is drawn with: what its projection wrote, or else its own.
        public static float RotateOf(VisualElement element, LayoutIdProjection? projection) =>
            projection is { WritesRotate: true } ? projection.DrawnRotate : element.resolvedStyle.rotate.angle.ToDegrees();

        public static Vector4 RadiiOf(VisualElement element, LayoutIdProjection? projection) =>
            projection is { WritesRadii: true } ? projection.DrawnRadii : ResolvedRadii(element);

        private static Vector4 ResolvedRadii(VisualElement element)
        {
            var resolved = element.resolvedStyle;
            return new Vector4(resolved.borderTopLeftRadius, resolved.borderTopRightRadius,
                resolved.borderBottomRightRadius, resolved.borderBottomLeftRadius);
        }

        // A slot that no longer holds the last value written here was written by someone else, and that value is the
        // element's own from then on, as AdoptForeignWrites takes the transform; a radius slot corner by corner.
        public static void Adopt(VisualElement element, LayoutIdProjection projection)
        {
            var rotate = element.style.rotate;
            if (projection.WritesRotate && rotate != projection.WrittenRotate)
            {
                projection.OwnInlineRotate = rotate;
                projection.OwnRotate = rotate.value.angle.ToDegrees();
            }
            if (!projection.WritesRadii) return;
            for (var corner = 0; corner < 4; corner++)
            {
                var slot = CornerRadiusFit.InlineCorner(element.style, corner);
                if (slot != new StyleLength(projection.WrittenRadii[corner])) projection.OwnInlineRadii[corner] = slot;
            }
        }

        // The element's own rotate: what the projection keeps for it while it writes it, and otherwise its inline
        // rotate or its resolved one, which then holds nothing of the projection's.
        public static float OwnRotate(VisualElement element, LayoutIdProjection projection)
        {
            if (projection.WritesRotate) return projection.OwnRotate;
            var inline = element.style.rotate;
            return inline.keyword == StyleKeyword.Undefined ? inline.value.angle.ToDegrees() : element.resolvedStyle.rotate.angle.ToDegrees();
        }

        // The element's own radii as they are now. While the projection writes the slots a corner's own is read from
        // what gives it rather than from the element: the radius CornerRadiusFit fits it to, an inline value the
        // projection took over, or the one the element's rules cascade to, in that order, and the one it was drawn
        // with as the projection began where none of those reads.
        public static Vector4 OwnRadii(VisualElement element, LayoutIdProjection projection)
        {
            if (!projection.WritesRadii) return ResolvedRadii(element);
            var own = projection.StartRadii;
            for (var corner = 0; corner < 4; corner++)
            {
                var inline = projection.OwnInlineRadii[corner];
                var radius = CornerRadiusFit.Fitted(element, corner);
                if (float.IsNaN(radius))
                {
                    radius = inline.keyword == StyleKeyword.Undefined && inline.value.unit == LengthUnit.Pixel
                        ? inline.value.value
                        : StyleCascade.Radius(element, corner);
                }
                if (!float.IsNaN(radius)) own[corner] = radius;
            }
            return own;
        }

        // Draws the element at the given rotate, or at its own for NaN. The slot is left alone until the projection
        // first needs a value there other than the element's own.
        public static void WriteRotate(VisualElement element, LayoutIdProjection projection, float rotate)
        {
            if (!projection.WritesRotate)
            {
                var own = OwnRotate(element, projection);
                if (float.IsNaN(rotate) || Mathf.Approximately(rotate, own)) return;
                projection.WritesRotate = true;
                projection.OwnInlineRotate = element.style.rotate;
                projection.OwnRotate = own;
                MotionNativeTransitionGuard.SuspendIfIntercepted(element, projection, MotionTransitionSlots.Rotate);
            }
            projection.DrawnRotate = float.IsNaN(rotate) ? projection.OwnRotate : rotate;
            element.style.rotate = new Rotate(new Angle(projection.DrawnRotate, AngleUnit.Degree));
            projection.WrittenRotate = element.style.rotate;
        }

        // Draws the element with the given radii on screen, or its own for null, divided by the scale it is drawn at
        // so that the scale leaves them as given, as Framer's correctBorderRadius does. A corner holds a single length
        // rather than the two radii of an ellipse, so a scale different on each axis is divided out by the geometric
        // mean of the two. A box drawn with no extent on an axis is written no radius, which is what the scale leaves
        // of any radius on screen.
        public static void WriteRadii(VisualElement element, LayoutIdProjection projection, Vector4? radii, Vector2 scale)
        {
            var divisor = Mathf.Sqrt(scale.x * scale.y);
            if (!projection.WritesRadii)
            {
                var own = ResolvedRadii(element);
                var target = radii ?? own;
                if (target == Vector4.zero || target == own && Mathf.Approximately(divisor, 1f)) return;
                projection.WritesRadii = true;
                var style = element.style;
                projection.OwnInlineRadii = new[]
                {
                    CornerRadiusFit.InlineCorner(style, 0), CornerRadiusFit.InlineCorner(style, 1),
                    CornerRadiusFit.InlineCorner(style, 2), CornerRadiusFit.InlineCorner(style, 3),
                };
                projection.StartRadii = own;
                CornerRadiusFit.Hold(element);
                MotionNativeTransitionGuard.SuspendIfIntercepted(element, projection, MotionTransitionSlots.Length);
            }
            projection.DrawnRadii = radii ?? OwnRadii(element, projection);
            var written = divisor > 0f ? projection.DrawnRadii / divisor : Vector4.zero;
            element.style.borderTopLeftRadius = written.x;
            element.style.borderTopRightRadius = written.y;
            element.style.borderBottomRightRadius = written.z;
            element.style.borderBottomLeftRadius = written.w;
            projection.WrittenRadii = written;
        }

        // Hands back what the projection wrote here, and the radius slots to CornerRadiusFit, which refits them for
        // whatever changed meanwhile.
        public static void Restore(VisualElement element, LayoutIdProjection projection)
        {
            if (projection.WritesRotate) element.style.rotate = projection.OwnInlineRotate;
            if (!projection.WritesRadii) return;
            element.style.borderTopLeftRadius = projection.OwnInlineRadii[0];
            element.style.borderTopRightRadius = projection.OwnInlineRadii[1];
            element.style.borderBottomRightRadius = projection.OwnInlineRadii[2];
            element.style.borderBottomLeftRadius = projection.OwnInlineRadii[3];
            CornerRadiusFit.Unhold(element);
        }
    }
}
