using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

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

        [Test]
        public void Given_TheAspectRatioRulesTheSheetDeclares_When_ReadAsAspectPresets_Then_TheRatioIsTheDeclaredOne()
        {
            // Arrange
            var declared = Regex.Matches(Sheet("_state_variants.uss"), @"\.aspect-(?<name>square|video)\s*\{\s*aspect-ratio:\s*(?<ratio>[0-9.]+);")
                .Select(m => $"{m.Groups["name"].Value}={float.Parse(m.Groups["ratio"].Value, System.Globalization.CultureInfo.InvariantCulture)}");

            // Act
            var read = new[] { "square", "video" }.Select(name =>
            {
                var plan = MotionSpringClassParser.Resolve(new[] { "aspect-[1/1]" }, new[] { "aspect-" + name });
                return $"{name}={plan.Lengths?.Single().To}";
            });

            // Assert
            Assert.That(string.Join(";", read), Is.EqualTo(string.Join(";", declared)));
        }

        [Test]
        public void Given_TheSemanticColorRulesThePaletteSheetDeclares_When_ListedBesideTheParsersTable_Then_TheyAreTheSameClassesOnTheSameProperties()
        {
            // Arrange
            var declared = Regex.Matches(Sheet("_palette.uss"),
                    @"^\.(?<cls>(?:bg|text|border)-(?<name>[a-z-]+))\s*\{\s*(?:background-color|color|border-color):\s*var\((?<var>--color-[a-z-]+)\);\s*\}",
                    RegexOptions.Multiline)
                .Where(m => !VelvetPalette.TryResolveColorToken(m.Groups["name"].Value, out _))
                .Select(m => $"{m.Groups["cls"].Value}={m.Groups["var"].Value}");

            // Act
            var table = MotionPropertyClassParser.ThemeColors.Select(pair => $"{pair.Key}={pair.Value}");

            // Assert
            Assert.That(string.Join(";", table.OrderBy(x => x, System.StringComparer.Ordinal)),
                Is.EqualTo(string.Join(";", declared.OrderBy(x => x, System.StringComparer.Ordinal))));
        }

        [Test]
        public void Given_SemanticColorUtilitiesOnAnElementInAPanel_When_ASwapNamesThem_Then_TheChannelRunsBetweenTheirResolvedColors()
        {
            // Arrange
            using var host = new HeadlessEditorPanelHost();
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss");
            host.Root.styleSheets.Add(sheet);
            var (element, surface, primary) = (new VisualElement(), new VisualElement(), new VisualElement());
            element.AddToClassList("bg-surface");
            surface.AddToClassList("bg-surface");
            primary.AddToClassList("bg-primary");
            host.Root.Add(element);
            host.Root.Add(surface);
            host.Root.Add(primary);
            EditorPanelTestHelpers.ForcePanelUpdate(host.Panel);
            var (from, to) = (new[] { "bg-surface" }, new[] { "bg-primary" });
            var context = MotionSlotContext.Read(element, from, to, _ => false);

            // Act
            var plan = MotionSpringClassParser.Resolve(from, to, 0f, 0f, context);

            // Assert
            Assert.That(plan.Colors is { Count: 1 } colors ? ((Color, Color)?)(colors[0].From, colors[0].To) : null,
                Is.EqualTo(((Color, Color)?)(surface.resolvedStyle.backgroundColor, primary.resolvedStyle.backgroundColor)));
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
