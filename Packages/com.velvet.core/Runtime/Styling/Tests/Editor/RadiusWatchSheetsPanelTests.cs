using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins which rules of the stylesheets above a mount hold the custom property that makes UI Toolkit send an element its
    /// style passes (<c>RadiusWatchSheets</c>): the rules that declare a border radius, and no other.
    /// </summary>
    internal sealed class RadiusWatchSheetsPanelTests : PanelTestBase
    {
        private const string Watch = "--velvet-radius-watch";
        private const string OwnSheet = "Packages/com.velvet.core/Runtime/Reconciler/Tests/Editor/LayoutIdTestRules.uss";
        private const string StylesDirectory = "Packages/com.velvet.core/Runtime/Styles";

        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        private StyleSheet AttachOwnSheet()
        {
            var sheet = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(OwnSheet);
            _window.rootVisualElement.styleSheets.Add(sheet);
            return sheet;
        }

        private void MountAndSettle()
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "box"));
            ForcePanelUpdate(_window.rootVisualElement.panel);
        }

        private static IEnumerable<string[]> PropertyNamesPerRule(StyleSheet sheet)
        {
            var rules = (Array)EngineMember.StyleSheetRules.ResolveProperty()!.GetValue(sheet);
            var properties = EngineMember.StyleRuleProperties.ResolveProperty()!;
            var name = EngineMember.StylePropertyName.ResolveProperty()!;
            foreach (var rule in rules)
            {
                yield return ((Array)properties.GetValue(rule)).Cast<object>().Select(p => (string)name.GetValue(p)).ToArray();
            }
        }

        private static bool DeclaresRadius(string[] names)
            => names.Any(n => n == "border-radius" || (n.StartsWith("border-") && n.EndsWith("-radius")));

        [Test]
        public void Given_AUserSheetAboveAMount_When_TheTreeMounts_Then_EveryRuleDeclaringARadiusHoldsTheWatchProperty()
        {
            // Arrange
            var sheet = AttachOwnSheet();

            // Act
            MountAndSettle();

            // Assert
            var lacking = PropertyNamesPerRule(sheet).Count(names => DeclaresRadius(names) && !names.Contains(Watch));
            Assert.That(lacking, Is.Zero);
        }

        // GREEN_ON_BASE(characterization): the base writes the property into no rule.
        [Test]
        public void Given_AUserSheetAboveAMount_When_TheTreeMounts_Then_NoRuleWithoutARadiusHoldsTheWatchProperty()
        {
            // Arrange
            var sheet = AttachOwnSheet();

            // Act
            MountAndSettle();

            // Assert
            var surplus = PropertyNamesPerRule(sheet).Count(names => !DeclaresRadius(names) && names.Contains(Watch));
            Assert.That(surplus, Is.Zero);
        }

        [Test]
        public void Given_AUserSheetAboveAMount_When_TwoTreesMountOnIt_Then_EachRuleHoldsTheWatchPropertyOnce()
        {
            // Arrange
            var sheet = AttachOwnSheet();
            MountAndSettle();
            var other = new VisualElement();
            _window.rootVisualElement.Add(other);
            var second = V.Mount(other, V.Div(name: "second"));

            // Act
            ForcePanelUpdate(_window.rootVisualElement.panel);
            second.Dispose();

            // Assert
            var repeated = PropertyNamesPerRule(sheet).Count(names => names.Count(n => n == Watch) > 1);
            Assert.That(repeated, Is.Zero);
        }

        [Test]
        public void Given_AUserSheetCarryingTheWatchProperty_When_ItIsTakenBack_Then_NoRuleHoldsIt()
        {
            // Arrange
            var sheet = AttachOwnSheet();
            MountAndSettle();

            // Act
            RadiusWatchSheets.TakeBack();

            // Assert
            var held = PropertyNamesPerRule(sheet).Count(names => names.Contains(Watch));
            Assert.That(held, Is.Zero);
        }

        // GREEN_ON_BASE(characterization): the base writes the property into no sheet, and the bundled utilities are left to
        // their restating rules.
        [Test]
        public void Given_TheBundledUtilitiesAboveAMount_When_TheTreeMounts_Then_NoRuleOfThemHoldsTheWatchProperty()
        {
            // Arrange
            var bundled = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss");

            // Act
            MountAndSettle();

            // Assert
            var held = PropertyNamesPerRule(bundled).Count(names => names.Contains(Watch));
            Assert.That(held, Is.Zero);
        }

        // GREEN_ON_BASE(construction): both sides are the repository's own sheets. Delete a rule from _radius_declared.uss
        // and this reddens.
        [Test]
        public void Given_TheBundledSheets_When_TheirRadiusRulesAreRead_Then_EachClassHasARestatingRuleOfCustomProperties()
        {
            // Arrange
            var comment = new Regex(@"/\*.*?\*/", RegexOptions.Singleline);
            var rule = new Regex(@"([^{}]+)\{([^{}]*)\}");
            var radius = new Regex(@"(?:^|[;\s])border(?:-(?:top|bottom)-(?:left|right))?-radius\s*:");
            var cls = new Regex(@"\.([A-Za-z_][A-Za-z0-9_-]*)");
            string Text(string name) => comment.Replace(File.ReadAllText($"{StylesDirectory}/{name}"), " ");
            var declaring = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var path in Directory.GetFiles(StylesDirectory, "*.uss").Where(p => !p.EndsWith("_radius_declared.uss")))
            {
                foreach (Match match in rule.Matches(comment.Replace(File.ReadAllText(path), " ")))
                {
                    if (radius.IsMatch(match.Groups[2].Value))
                    {
                        foreach (Match name in cls.Matches(match.Groups[1].Value))
                        {
                            declaring.Add(name.Groups[1].Value);
                        }
                    }
                }
            }
            var restated = new SortedSet<string>(rule.Matches(Text("_radius_declared.uss")).Cast<Match>()
                .Where(match => match.Groups[2].Value.Contains("--velvet-radius-"))
                .SelectMany(match => cls.Matches(match.Groups[1].Value).Cast<Match>().Select(name => name.Groups[1].Value)),
                StringComparer.Ordinal);

            // Act
            var unrestated = declaring.Where(name => !restated.Contains(name)).ToList();
            if (declaring.Count == 0)
            {
                unrestated.Add("no radius rule was read");
            }

            // Assert
            Assert.That(unrestated, Is.Empty);
        }
    }
}
