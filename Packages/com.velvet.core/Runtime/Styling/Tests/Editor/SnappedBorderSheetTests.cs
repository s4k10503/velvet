using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the border-width rules <see cref="SnappedBorderSheets"/> snaps against the ones <c>_borders.uss</c> declares:
    /// the same classes, writing the same sides at the same widths, so that a stylesheet that moves fails here rather
    /// than leaving the snapped sheet to outrank it with stale rules.
    /// </summary>
    internal sealed class SnappedBorderSheetTests : PanelTestBase
    {
        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        private static IEnumerable<TestCaseData> Snapped()
            => SnappedBorderSheets.Rules().Select(rule => new TestCaseData(rule.Class, rule.Pixels, rule.Properties)
                .SetName($"Given_TheSnappedRuleOf_{rule.Class}_When_TheClassIsMounted_Then_ItWritesTheSnappedRulesSidesAtItsWidth"));

        [TestCaseSource(nameof(Snapped))]
        public void Given_TheSnappedRuleOfABundledClass_When_TheClassIsMounted_Then_ItWritesTheSnappedRulesSidesAtItsWidth(
            string cls, int pixels, string[] properties)
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "box", className: "w-[50px] h-[50px] " + cls));
            var box = _window.rootVisualElement.Q<VisualElement>("box");
            ForcePanelUpdate(box.panel);
            var style = box.resolvedStyle;

            // Act
            var widths = new[] { style.borderTopWidth, style.borderRightWidth, style.borderBottomWidth, style.borderLeftWidth };

            // Assert
            // The editor's own display scale decides whether the snapped sheet follows the bundled one here, so the
            // widths are the bundled ones as that scale snaps them.
            var scale = box.panel.scaledPixelsPerPoint;
            var expected = new[] { "border-top-width", "border-right-width", "border-bottom-width", "border-left-width" }
                .Select(side => properties.Contains(side) ? StyleRelativeLengths.SnapToDevicePixels(pixels, scale) : 0f).ToArray();
            Assert.That(widths, Is.EqualTo(expected));
        }

        [Test]
        public void Given_TheBundledWidthClasses_When_TheStylesheetsAreRead_Then_TheSnappedRulesAreThoseClasses()
        {
            // Arrange
            var declared = new HashSet<string>();
            var pending = new Stack<StyleSheet>();
            pending.Push(VelvetStyleUtilities.Sheet);
            while (pending.Count > 0)
            {
                var sheet = pending.Pop();
                foreach (var import in VelvetStyleUtilities.ImportsOf(sheet))
                {
                    pending.Push(import);
                }
                foreach (var rule in (System.Array)EngineMember.StyleSheetRules.ResolveProperty()!.GetValue(sheet))
                {
                    AddWidthClasses(rule, declared);
                }
            }

            // Act
            var snapped = SnappedBorderSheets.Rules().Select(rule => rule.Class).OrderBy(name => name).ToArray();

            // Assert
            Assert.That(snapped, Is.EqualTo(declared.OrderBy(name => name).ToArray()));
        }

        private static void AddWidthClasses(object rule, HashSet<string> into)
        {
            var properties = (System.Array)EngineMember.StyleRuleProperties.ResolveProperty()!.GetValue(rule);
            var writesWidth = properties.Cast<object>().Any(property =>
                ((string)EngineMember.StylePropertyName.ResolveProperty()!.GetValue(property)).EndsWith("-width"));
            if (!writesWidth)
            {
                return;
            }
            foreach (var complex in (System.Array)EngineMember.StyleRuleComplexSelectors.ResolveProperty()!.GetValue(rule))
            {
                var selectors = (System.Array)EngineMember.StyleComplexSelectorSelectors.ResolveProperty()!.GetValue(complex);
                var parts = (System.Array)EngineMember.StyleSelectorParts.ResolveProperty()!.GetValue(selectors.GetValue(0));
                var name = (string)EngineMember.StyleSelectorPartValue.ResolveProperty()!.GetValue(parts.GetValue(0));
                if (selectors.Length == 1 && parts.Length == 1 && name.StartsWith("border"))
                {
                    into.Add(name);
                }
            }
        }
    }
}
