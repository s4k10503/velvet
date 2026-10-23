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

    // The shape a skewed element is picked by: its box with each corner taken to where the element is drawn
    // (worldTransform) and back through WorldToLocal, which is how a pointer arrives. SkewTransformPlaybackTests holds
    // the outcome to a pointer over the drawn shape.
    internal sealed class SkewPickRegion
    {
        public bool Contains(VisualElement element, Vector2 local)
        {
            var width = element.layout.width;
            var height = element.layout.height;
            if (!(width > 0f && height > 0f))
            {
                return false;
            }
            var q0 = Corner(element, new Vector2(0f, 0f));
            var q1 = Corner(element, new Vector2(width, 0f));
            var q2 = Corner(element, new Vector2(width, height));
            var q3 = Corner(element, new Vector2(0f, height));
            var s0 = Side(q0, q1, local);
            var s1 = Side(q1, q2, local);
            var s2 = Side(q2, q3, local);
            var s3 = Side(q3, q0, local);
            var allNonNegative = s0 >= 0f && s1 >= 0f && s2 >= 0f && s3 >= 0f;
            var allNonPositive = s0 <= 0f && s1 <= 0f && s2 <= 0f && s3 <= 0f;
            return allNonNegative || allNonPositive;
        }

        private static Vector2 Corner(VisualElement element, Vector2 corner)
        {
            var world = element.worldTransform.MultiplyPoint3x4(new Vector3(corner.x, corner.y, 0f));
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
