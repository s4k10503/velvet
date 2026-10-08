using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Holds the skew silhouette shader's stop arrays to <c>GradientSpec.MaxStops</c>, the most stops the
    /// parser lets through and the length <c>GradientSilhouetteBaker</c> sends. Read off the shader source,
    /// since the declared length is not something a bake reports. Also holds the parser's per-class memo
    /// to its bound, read by reflection.
    /// </summary>
    [TestFixture]
    internal sealed class GradientStopCapacityTests
    {
        private const string ShaderPath = "Packages/com.velvet.core/Runtime/Styles/Shaders/VelvetGradientSilhouette.shader";

        [Test]
        public void Given_TheSilhouetteShader_When_ItsStopArraysAreRead_Then_EachHoldsMaxStops()
        {
            // Arrange
            var source = File.ReadAllText(Path.GetFullPath(ShaderPath));

            // Act
            var lengths = DeclaredLength(source, "_StopColors") + "," + DeclaredLength(source, "_StopPositions");

            // Assert
            Assert.That(lengths, Is.EqualTo(GradientSpec.MaxStops + "," + GradientSpec.MaxStops));
        }

        [Test]
        public void Given_MoreDistinctActivatorsThanTheMemoHolds_When_Extracted_Then_TheMemoStaysWithinItsBound()
        {
            // Arrange — the parser's memo of activator readings, emptied, then sent one more distinct class
            // than the 256 its bound admits.
            var memo = typeof(StyleGradientClass).GetField("s_activators", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);
            var entries = (IDictionary)memo.GetType().GetField("_entries", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(memo);
            entries.Clear();

            // Act
            for (var angle = 0; angle <= 256; angle++)
            {
                StyleGradientClass.TryExtract(new[] { "bg-linear-" + angle, "from-[#ff0000]" }, out _);
            }

            // Assert
            Assert.That(entries.Count, Is.LessThanOrEqualTo(256));
        }

        // The array's declared length, written either as a literal or as a #define name; -1 when neither reads.
        private static int DeclaredLength(string source, string array)
        {
            var declaration = Regex.Match(source, @"\bfloat\d?\s+" + array + @"\s*\[\s*(\w+)\s*\]\s*;");
            if (!declaration.Success)
            {
                return -1;
            }
            var size = declaration.Groups[1].Value;
            if (int.TryParse(size, NumberStyles.None, CultureInfo.InvariantCulture, out var literal))
            {
                return literal;
            }
            var define = Regex.Match(source, @"#define\s+" + size + @"\s+(\d+)");
            return define.Success ? int.Parse(define.Groups[1].Value, CultureInfo.InvariantCulture) : -1;
        }
    }
}
