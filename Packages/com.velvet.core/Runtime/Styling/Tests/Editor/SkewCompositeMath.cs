using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    // The matrix a skewed element's two inline slots draw, and the matrix CSS composes for the same rotate, scale and
    // skew, so a case can compare what was written with what was meant.
    internal static class SkewCompositeMath
    {
        // The top-left block of the slots' rotation with its columns scaled, as [m00, m01, m10, m11].
        public static float[] Drawn(VisualElement element)
        {
            var rotate = element.style.rotate.value;
            var carried = Matrix4x4.Rotate(Quaternion.AngleAxis(rotate.angle.ToDegrees(), AxisOf(rotate)));
            var scale = element.style.scale.value.value;
            return new[] { scale.x * carried.m00, scale.y * carried.m01, scale.x * carried.m10, scale.y * carried.m11 };
        }

        // The rotation axis of a Rotate, which the engine's struct keeps without a public reader.
        private static Vector3 AxisOf(Rotate rotate)
        {
            foreach (var field in typeof(Rotate).GetFields(
                         System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                         | System.Reflection.BindingFlags.NonPublic))
            {
                if (field.FieldType == typeof(Vector3))
                {
                    return (Vector3)field.GetValue(rotate);
                }
            }
            return Vector3.forward;
        }

        // rotate(theta) * scale(sx, sy) * skewX(x) skewY(y), as [m00, m01, m10, m11].
        public static float[] Composite(float rotateDeg, float scaleX, float scaleY, float xDeg, float yDeg)
        {
            var tx = Mathf.Tan(xDeg * Mathf.Deg2Rad);
            var ty = Mathf.Tan(yDeg * Mathf.Deg2Rad);
            var cos = Mathf.Cos(rotateDeg * Mathf.Deg2Rad);
            var sin = Mathf.Sin(rotateDeg * Mathf.Deg2Rad);
            var p00 = cos * scaleX;
            var p01 = -sin * scaleY;
            var p10 = sin * scaleX;
            var p11 = cos * scaleY;
            var k = 1f + (tx * ty);
            return new[] { (p00 * k) + (p01 * ty), (p00 * tx) + p01, (p10 * k) + (p11 * ty), (p10 * tx) + p11 };
        }
    }
}
