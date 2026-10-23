using UnityEngine;

namespace Velvet
{
    // The linear part of CSS `skewX(x) skewY(y)` written as what a UI Toolkit transform can carry: a uniform scale
    // and one 3D rotation, which the engine projects onto the panel. A 2x2 matrix is a rotation, a stretch along
    // two axes and another rotation (its singular value decomposition); the stretch is a uniform scale by the larger
    // factor followed by a turn about the in-plane axis that foreshortens the other direction by the ratio, so the
    // whole is rotation * rotation-about-x * rotation, with the z column of it never drawn.
    internal static class SkewTransformFactor
    {
        public static bool TryFactor(float xDeg, float yDeg, out Quaternion rotation, out float scale)
        {
            var tx = Mathf.Tan(xDeg * Mathf.Deg2Rad);
            var ty = Mathf.Tan(yDeg * Mathf.Deg2Rad);
            // skewX(x) * skewY(y)
            var ok = TryFactor(1f + (tx * ty), tx, ty, 1f, out rotation, out var drawn);
            scale = drawn.x;
            return ok;
        }

        // The matrix [[a, b], [c, d]] as the pair of slots that draw it: scale.x * Matrix4x4.Rotate(rotation) has it
        // as its top-left block. A rotation cannot reflect, so a matrix of negative determinant has its second column
        // negated before it is factored and that reflection goes to the scale's y, which CSS `scale` takes negative.
        // False for a matrix that collapses the plane or holds a value no float carries.
        public static bool TryFactor(float a, float b, float c, float d, out Quaternion rotation, out Vector2 scale)
        {
            rotation = Quaternion.identity;
            scale = Vector2.one;
            var determinant = (a * d) - (b * c);
            if (float.IsNaN(determinant) || float.IsInfinity(determinant) || Mathf.Abs(determinant) <= 1e-6f)
            {
                return false;
            }
            var reflected = determinant < 0f;
            if (reflected)
            {
                b = -b;
                d = -d;
            }
            var e = (a + d) * 0.5f;
            var f = (a - d) * 0.5f;
            var g = (c + b) * 0.5f;
            var h = (c - b) * 0.5f;
            var q = Mathf.Sqrt((e * e) + (h * h));
            var r = Mathf.Sqrt((f * f) + (g * g));
            var larger = q + r;
            var smaller = q - r;
            var a1 = Mathf.Atan2(g, f);
            var a2 = Mathf.Atan2(h, e);
            var before = (a2 - a1) * 0.5f;
            var after = (a2 + a1) * 0.5f;
            var foreshorten = Mathf.Acos(Mathf.Clamp01(smaller / larger));
            rotation = Quaternion.AngleAxis(after * Mathf.Rad2Deg, Vector3.forward)
                * Quaternion.AngleAxis(foreshorten * Mathf.Rad2Deg, Vector3.right)
                * Quaternion.AngleAxis(before * Mathf.Rad2Deg, Vector3.forward);
            scale = new Vector2(larger, reflected ? -larger : larger);
            return true;
        }
    }
}
