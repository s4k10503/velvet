using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that the values BundledClassTransform reads off a class name are the ones the bundled sheet's rule for
    /// that class declares.
    /// </summary>
    [TestFixture]
    internal sealed class BundledClassTransformTests
    {
        [Test]
        public void Given_TheBundledScaleRotateAndOriginClasses_When_TheirNamesAreRead_Then_EachGivesWhatItsSheetRuleDeclares()
        {
            // Arrange
            var sheet = File.ReadAllText(Path.GetFullPath("Packages/com.velvet.core/Runtime/Styles/_transforms.uss"));
            var read = StyleUtilityTableProbe.ClassNames().Select(Declaration).Where(declaration => declaration != null).ToArray();

            // Act
            var wrong = read.Where(declaration => !sheet.Contains(declaration));

            // Assert
            Assert.That((read.Length > 0, string.Join("; ", wrong)), Is.EqualTo((true, string.Empty)));
        }

        // The rule the sheet would hold for the class, as the reader gives its value; null for a class it reads nothing off.
        private static string Declaration(string name)
        {
            if (BundledClassTransform.TryScaleOf(name, out var scale))
            {
                var factor = scale.ToString(CultureInfo.InvariantCulture);
                return $".{name} {{ scale: {factor} {factor}; }}";
            }
            if (BundledClassTransform.TryRotateOf(name, out var degrees))
            {
                return $".{name} {{ rotate: {degrees.ToString(CultureInfo.InvariantCulture)}deg; }}";
            }
            if (BundledClassTransform.TryOriginOf(name, out var origin))
            {
                return $".{name} {{ transform-origin: {Side(origin.x, "left", "right")} {Side(origin.y, "top", "bottom")}; }}";
            }
            return null;
        }

        private static string Side(float fraction, string start, string end) =>
            fraction == 0f ? start : fraction == 1f ? end : "center";
    }
}
