#nullable enable
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // The element types Velvet creates whose ContainsPoint picks by a skew's shape. The override is on the type; the shape
    // is data on the element (SkewPickRegion), set while a skew-* class is on it and null otherwise, where the override
    // falls back to the rectangle.
    internal interface ISkewPickTarget
    {
        SkewPickRegion? PickRegion { get; set; }
    }

    // The shape a skewed element is picked by: its box sheared by the tangents it paints with, or the box alone where
    // the shear is the element's transform. Each corner is taken to where the element is drawn (worldTransform) and
    // back through WorldToLocal; SkewTransformPlaybackTests holds the outcome to a pointer over the drawn shape where the
    // shear is the element's transform.
    internal sealed class SkewPickRegion
    {
        public float TanX;
        public float TanY;

        public bool Contains(VisualElement element, Vector2 local)
        {
            var width = element.layout.width;
            var height = element.layout.height;
            if (!(width > 0f && height > 0f))
            {
                return false;
            }
            var q0 = Corner(element, new Vector2(0f, 0f), width, height);
            var q1 = Corner(element, new Vector2(width, 0f), width, height);
            var q2 = Corner(element, new Vector2(width, height), width, height);
            var q3 = Corner(element, new Vector2(0f, height), width, height);
            var s0 = Side(q0, q1, local);
            var s1 = Side(q1, q2, local);
            var s2 = Side(q2, q3, local);
            var s3 = Side(q3, q0, local);
            var allNonNegative = s0 >= 0f && s1 >= 0f && s2 >= 0f && s3 >= 0f;
            var allNonPositive = s0 <= 0f && s1 <= 0f && s2 <= 0f && s3 <= 0f;
            return allNonNegative || allNonPositive;
        }

        private Vector2 Corner(VisualElement element, Vector2 corner, float width, float height)
        {
            var sheared = SilhouetteFace.Shear(corner, width, height, TanX, TanY);
            var world = element.worldTransform.MultiplyPoint3x4(new Vector3(sheared.x, sheared.y, 0f));
            return element.WorldToLocal(new Vector2(world.x, world.y));
        }

        // Which side of the directed edge from a to b the point lies on, by the sign of the cross product.
        private static float Side(Vector2 a, Vector2 b, Vector2 point)
            => ((b.x - a.x) * (point.y - a.y)) - ((b.y - a.y) * (point.x - a.x));
    }

    internal sealed class SkewPickElement : VisualElement, ISkewPickTarget
    {
        public SkewPickRegion? PickRegion { get; set; }

        public override bool ContainsPoint(Vector2 localPoint)
            => PickRegion != null ? PickRegion.Contains(this, localPoint) : base.ContainsPoint(localPoint);
    }

    internal sealed class SkewPickButton : Button, ISkewPickTarget
    {
        public SkewPickRegion? PickRegion { get; set; }

        public override bool ContainsPoint(Vector2 localPoint)
            => PickRegion != null ? PickRegion.Contains(this, localPoint) : base.ContainsPoint(localPoint);
    }

    internal sealed class SkewPickLabel : Label, ISkewPickTarget
    {
        public SkewPickLabel(string text)
            : base(text)
        {
        }

        public SkewPickRegion? PickRegion { get; set; }

        public override bool ContainsPoint(Vector2 localPoint)
            => PickRegion != null ? PickRegion.Contains(this, localPoint) : base.ContainsPoint(localPoint);
    }
}
