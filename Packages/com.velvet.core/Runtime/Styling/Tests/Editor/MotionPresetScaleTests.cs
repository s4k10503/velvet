using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the preset names a spring or bezier delta reads a magnitude from — the font-size and letter-spacing
    /// scales, <c>rounded-full</c> and the <c>-full</c> keyword lengths — against the bundled stylesheets that
    /// declare them.
    /// </summary>
    [TestFixture]
    internal sealed class MotionPresetScaleTests
    {
        private static string Sheet(string name)
            => File.ReadAllText(Path.GetFullPath("Packages/com.velvet.core/Runtime/Styles/" + name));

        private static string DrivenTarget(string fromClass, string toClass)
        {
            var plan = MotionSpringClassParser.Resolve(new[] { fromClass }, new[] { toClass });
            return plan.Lengths == null ? "none" : string.Join(",", plan.Lengths.Select(l => $"{l.To}{l.Unit}").Distinct());
        }

        [Test]
        public void Given_EveryFontSizeTheTokenSheetDeclares_When_ReadAsATextPreset_Then_TheMagnitudeIsTheDeclaredSize()
        {
            // Arrange
            var declared = Regex.Matches(Sheet("_tokens.uss"), @"--text-(?<name>[0-9a-z]+):\s*(?<px>\d+)px")
                .Select(m => $"{m.Groups["name"].Value}={m.Groups["px"].Value}Pixel");

            // Act
            var read = declared.Select(d => d.Split('=')[0])
                .Select(name => $"{name}={DrivenTarget("text-[1px]", "text-" + name)}");

            // Assert
            Assert.That(string.Join(";", read), Is.EqualTo(string.Join(";", declared)));
        }

        [Test]
        public void Given_EveryTrackingStepTheTypographySheetDeclares_When_ReadAsATrackingPreset_Then_TheMagnitudeIsTheDeclaredSpacing()
        {
            // Arrange
            var declared = Regex.Matches(Sheet("_typography.uss"), @"\.tracking-(?<name>[a-z]+)\s*\{\s*letter-spacing:\s*(?<px>-?[0-9.]+)(px)?;")
                .Select(m => (name: m.Groups["name"].Value, px: float.Parse(m.Groups["px"].Value, System.Globalization.CultureInfo.InvariantCulture)))
                .ToList();

            // Act
            var read = declared.Select(d => (d.name, target: DrivenTarget("tracking-[3px]", "tracking-" + d.name)));

            // Assert
            Assert.That(string.Join(";", read.Select(r => $"{r.name}={r.target}")),
                Is.EqualTo(string.Join(";", declared.Select(d => $"{d.name}={d.px}Pixel"))));
        }

        [Test]
        public void Given_TheFullRadiusTheTokenSheetDeclares_When_ReadAsRoundedFull_Then_EveryCornerEndsThere()
        {
            // Arrange
            var declared = Regex.Match(Sheet("_tokens.uss"), @"--radius-full:\s*(?<px>\d+)px").Groups["px"].Value;

            // Act
            var target = DrivenTarget("rounded-none", "rounded-full");

            // Assert
            Assert.That(target, Is.EqualTo(declared + "Pixel"));
        }

        [TestCase("w-", "Width")]
        [TestCase("min-h-", "MinHeight")]
        [TestCase("left-", "Left")]
        public void Given_AFullKeywordLength_When_ReadAsATarget_Then_ItIsOneHundredPercent(string prefix, string property)
        {
            // Act
            var plan = MotionSpringClassParser.Resolve(new[] { prefix + "[50%]" }, new[] { prefix + "full" });

            // Assert
            Assert.That(string.Join(",", plan.Lengths?.Select(l => $"{l.Property}={l.To}{l.Unit}") ?? Enumerable.Empty<string>()),
                Is.EqualTo($"{property}=100Percent"));
        }
    }
}
