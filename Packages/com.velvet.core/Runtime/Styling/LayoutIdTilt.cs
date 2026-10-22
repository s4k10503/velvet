#nullable enable
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // A projected Motion drawn at a rotate that is not a quarter turn inside a parent drawn at a different scale on each
    // axis. Framer draws it rotated and unsheared at its natural size: its transform puts scale(1/treeScale) outside the
    // rotate, which takes the parent's stretch out in the parent's axes. An element's own rotate and scale compose the
    // other way round (VisualElement's pivoted matrix is a translate, then the rotate, then the scale), and no rotate about
    // the axis out of the panel and scale undo a stretch at an angle to the element's axes. A rotate about an axis in the
    // panel's plane does: it tilts the element, and a screen panel draws it flattened, its sides foreshortened to the
    // lengths the stretch needs
    // (Given_AnElementTiltedOutOfAScreenPanel_When_ItIsDrawn_Then_ItIsDrawnFlattened,
    // Given_ALayoutIdMotionTurnedInsideOneStretchedOnOneAxis_When_ItIsDrawn_Then_ItIsARotatedSquareAtItsOwnSize).
    //
    // The projection has written its rotate and scale for the pass before Correct replaces them; a pass that does not tilt
    // writes them as ever, so a tilt ends with the stretch, and the slots are handed back where the projection ends.
    internal static class LayoutIdTilt
    {
        private static readonly System.Reflection.PropertyInfo? s_isFlat = EngineMember.PanelIsFlat.ResolveProperty();

        public static void Correct(VisualElement element, LayoutIdProjection projection)
        {
            var parent = projection.ParentScale;
            if (!projection.WritesScale || Mathf.Approximately(parent.x, parent.y)) return;
            var spin = StyleAnimateDriver.CurrentSpinDeg(element);
            var drawn = projection.WritesRotate ? projection.DrawnRotate : LayoutIdLook.MixTarget(element, projection, 0f);
            if (IsQuarterTurn(drawn + spin) || !OnScreenPanel(element) || Clips(element)) return;
            if (!projection.WritesRotate)
            {
                LayoutIdLook.TakeRotate(element, projection, LayoutIdLook.OwnRotate(element, projection, 0f));
                projection.DrawnRotate = drawn;
            }
            var scale = element.style.scale.value.value;
            if (!TrySolve(drawn + spin, new Vector2(scale.x, scale.y), parent, out var rotation, out var axes)) return;
            element.style.rotate = new Rotate(rotation);
            element.style.scale = new Scale(new Vector3(axes.x, axes.y, scale.z));
            projection.WrittenRotate = element.style.rotate;
            projection.WrittenScale = element.style.scale;
        }

        // The rotation and the scale whose product, its depth dropped, is P⁻¹·R(degrees)·P·S: the element's own matrix that,
        // inside a parent drawn at P, draws it at R·P·S, its rotate in the panel's axes at the size P·S gives it. Their
        // columns l1 and l2 are the images of the element's x and y axes; the rotation takes those axes to unit vectors
        // u and v whose in-plane parts are l1/sx and l2/sy and whose depths cancel u·v, at the depth that tilts both alike.
        // False where an axis has no length.
        internal static bool TrySolve(float degrees, Vector2 elementScale, Vector2 parentScale, out Quaternion rotation, out Vector2 axes)
        {
            rotation = Quaternion.identity;
            axes = Vector2.zero;
            var turn = Quaternion.AngleAxis(degrees, Vector3.forward);
            Vector2 Column(Vector2 axis) =>
                ((Vector2)(turn * (axis * parentScale * elementScale))) / parentScale;
            var l1 = Column(Vector2.right);
            var l2 = Column(Vector2.up);
            var (a, b) = (l1.magnitude, l2.magnitude);
            if (a <= 0f || b <= 0f || parentScale.x == 0f || parentScale.y == 0f) return false;
            var cos = Vector2.Dot(l1, l2) / (a * b);
            var plane = 1f / Mathf.Sqrt(1f + Mathf.Abs(cos));
            var depth = Mathf.Sqrt(1f - plane * plane);
            var u = new Vector3(l1.x / a * plane, l1.y / a * plane, depth);
            var v = new Vector3(l2.x / b * plane, l2.y / b * plane, cos > 0f ? -depth : depth);
            rotation = Quaternion.LookRotation(Vector3.Cross(u, v), v);
            axes = new Vector2(a / plane, b / plane);
            return true;
        }

        private static bool IsQuarterTurn(float degrees) => Mathf.Abs(Mathf.Repeat(degrees + 45f, 90f) - 45f) < 1e-3f;

        // Read once a panel, since a pass asks on every frame of a tilt and the read boxes its answer.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IPanel, object> s_flat = new();

        private static bool OnScreenPanel(VisualElement element)
        {
            if (s_isFlat == null || element.panel is not { } panel) return false;
            if (!s_flat.TryGetValue(panel, out var flat))
            {
                flat = s_isFlat.GetValue(panel);
                s_flat.Add(panel, flat);
            }
            return flat is true;
        }

        // Unity documents a 3D rotate as masking wrongly on a screen panel (Rotate(Angle, Vector3)'s remarks), so a Motion
        // that clips, or sits in or holds one that clips, keeps the approximation.
        private static bool Clips(VisualElement element)
        {
            for (var e = element.hierarchy.parent; e != null; e = e.hierarchy.parent)
            {
                if (StyleCascade.ClipsOverflow(e)) return true;
            }
            return ClipsWithin(element);
        }

        private static bool ClipsWithin(VisualElement element)
        {
            if (StyleCascade.ClipsOverflow(element)) return true;
            for (var i = 0; i < element.hierarchy.childCount; i++)
            {
                if (ClipsWithin(element.hierarchy[i])) return true;
            }
            return false;
        }
    }
}
