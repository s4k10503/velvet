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
            var a = 1f + (tx * ty);
            var b = tx;
            var c = ty;
            var d = 1f;
            var determinant = (a * d) - (b * c);
            if (!(determinant > 1e-6f) || float.IsInfinity(a) || float.IsNaN(a))
            {
                rotation = Quaternion.identity;
                scale = 1f;
                return false;
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
            scale = larger;
            return true;
        }
    }
}
