using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the runtime-only sheets <c>RadiusWatchSheets</c> derives from the stylesheets above a mount: one rule for each
    /// rule of the source that declares a border radius, holding the watch property and nothing else, attached beside the
    /// source, while the source itself stays as it was written.
    /// </summary>
    internal sealed class RadiusWatchSheetsPanelTests : PanelTestBase
    {
        private const string Watch = "--velvet-radius-watch";
        private const string OwnSheet = "Packages/com.velvet.core/Runtime/Reconciler/Tests/Editor/LayoutIdTestRules.uss";
        private const string NoRadiusSheet = "Packages/com.velvet.core/Runtime/Styling/Tests/Editor/ThemeTokenSheet.uss";
        private const string StylesDirectory = "Packages/com.velvet.core/Runtime/Styles";
        private const BindingFlags Statics = BindingFlags.NonPublic | BindingFlags.Static;

        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        private static StyleSheet Load(string path) => UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(path);

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

        private static List<StyleSheet> WatchSheetsOn(VisualElement element)
        {
            var found = new List<StyleSheet>();
            for (var i = 0; i < element.styleSheets.count; i++)
            {
                if (RadiusWatchSheets.IsWatch(element.styleSheets[i]))
                {
                    found.Add(element.styleSheets[i]);
                }
            }
            return found;
        }

        // One tick of the panel's watch over the stylesheets of its ancestors, which BorderPreflight owns.
        private static void TickSheetWatch(IPanel panel)
        {
            var table = typeof(BorderPreflight).GetField("s_sheetWatches", Statics)!.GetValue(null)!;
            var args = new object[] { panel, null! };
            table.GetType().GetMethod("TryGetValue")!.Invoke(table, args);
            typeof(BorderPreflight).GetMethod("TickSheets", Statics)!.Invoke(null, new[] { panel, args[1] });
        }

        [Test]
        public void Given_AUserSheetAboveAMount_When_TheTreeMounts_Then_TheRootHoldsAWatchSheetWithARulePerRadiusRule()
        {
            // Arrange
            var source = Load(OwnSheet);
            _window.rootVisualElement.styleSheets.Add(source);

            // Act
            MountAndSettle();

            // Assert
            var watch = WatchSheetsOn(_window.rootVisualElement).SelectMany(PropertyNamesPerRule).Count();
            Assert.That(watch, Is.EqualTo(PropertyNamesPerRule(source).Count(DeclaresRadius)).And.GreaterThan(0));
        }

        [Test]
        public void Given_AUserSheetAboveAMount_When_TheTreeMounts_Then_EveryRuleOfTheWatchSheetHoldsOnlyTheWatchProperty()
        {
            // Arrange
            _window.rootVisualElement.styleSheets.Add(Load(OwnSheet));

            // Act
            MountAndSettle();

            // Assert — a run with no watch rule at all reads -1, which no count of wrong rules equals.
            var rules = WatchSheetsOn(_window.rootVisualElement).SelectMany(PropertyNamesPerRule).ToList();
            var others = rules.Count(names => names.Length != 1 || names[0] != Watch);
            Assert.That(rules.Count == 0 ? -1 : others, Is.Zero);
        }

        [Test]
        public void Given_AUserSheetAboveAMount_When_APollAndAPanelUpdateFollowTheMount_Then_NoRuleOfTheSourceHoldsTheWatchProperty()
        {
            // Arrange — the source is the application's own asset: nothing may be written into it, since a tool that saves
            // a sheet would save it too.
            var source = Load(OwnSheet);
            _window.rootVisualElement.styleSheets.Add(source);
            MountAndSettle();

            // Act
            TickSheetWatch(_window.rootVisualElement.panel);
            EditorPanelTestHelpers.DriveSchedulerOnce(_window.rootVisualElement.panel);
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(PropertyNamesPerRule(source).Count(names => names.Contains(Watch)), Is.Zero);
        }

        [Test]
        public void Given_AUserSheetAboveAMount_When_TwoTreesMountOnIt_Then_TheRootHoldsOneWatchSheet()
        {
            // Arrange
            _window.rootVisualElement.styleSheets.Add(Load(OwnSheet));
            MountAndSettle();
            var other = new VisualElement();
            _window.rootVisualElement.Add(other);
            var second = V.Mount(other, V.Div(name: "second"));

            // Act
            ForcePanelUpdate(_window.rootVisualElement.panel);
            second.Dispose();

            // Assert
            Assert.That(WatchSheetsOn(_window.rootVisualElement).Count, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base attaches no watch sheet to any root.
        [Test]
        public void Given_ASheetWithNoRadiusRuleAboveAMount_When_TheTreeMounts_Then_NoWatchSheetIsAttached()
        {
            // Arrange
            _window.rootVisualElement.styleSheets.Add(Load(NoRadiusSheet));

            // Act
            MountAndSettle();

            // Assert
            Assert.That(WatchSheetsOn(_window.rootVisualElement).Count, Is.Zero);
        }

        // GREEN_ON_BASE(characterization): the base attaches no watch sheet to any root.
        [Test]
        public void Given_OnlyTheBundledUtilitiesAboveAMount_When_TheTreeMounts_Then_NoWatchSheetIsAttached()
        {
            // Act
            MountAndSettle();

            // Assert
            Assert.That(WatchSheetsOn(_window.rootVisualElement).Count, Is.Zero);
        }

        [Test]
        public void Given_AMountedTree_When_ASheetWithARadiusRuleIsAttachedToItsRootAfterwards_Then_ThePanelsWatchAttachesItsWatchSheet()
        {
            // Arrange — the tree below the root has laid out, which is what has the panel's watch compare the root's sheets.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "box", className: "w-[100px] h-[40px]"));
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Act
            _window.rootVisualElement.styleSheets.Add(Load(OwnSheet));
            TickSheetWatch(_window.rootVisualElement.panel);

            // Assert
            Assert.That(WatchSheetsOn(_window.rootVisualElement).Count, Is.EqualTo(1));
        }

        [Test]
        public void Given_ASheetAttachedToAnElementInsideTheTree_When_ThePanelsWatchTicks_Then_ThatElementHoldsItsWatchSheet()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "inner", children: new VNode?[]
            {
                V.Div(name: "leaf", className: "w-[100px] h-[40px]"),
            }));
            ForcePanelUpdate(_window.rootVisualElement.panel);
            var inner = _window.rootVisualElement.Q<VisualElement>("inner");

            // Act
            inner.styleSheets.Add(Load(OwnSheet));
            TickSheetWatch(inner.panel);

            // Assert
            Assert.That(WatchSheetsOn(inner).Count, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(construction): both sides are the repository's own sheets. Delete `.rounded-full` from
        // `_radius_declared.uss` and this reddens.
        [Test]
        public void Given_TheBundledSheets_When_TheirRadiusRulesAreRead_Then_EachClassHasARestatingRuleOfCustomProperties()
        {
            // Arrange
            var comment = new Regex(@"/\*.*?\*/", RegexOptions.Singleline);
            var rule = new Regex(@"([^{}]+)\{([^{}]*)\}");
            var radius = new Regex(@"(?:^|[;\s])border(?:-(?:top|bottom)-(?:left|right))?-radius\s*:");
            var cls = new Regex(@"\.([A-Za-z_][A-Za-z0-9_-]*)");
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
            var restated = new SortedSet<string>(
                rule.Matches(comment.Replace(File.ReadAllText($"{StylesDirectory}/_radius_declared.uss"), " ")).Cast<Match>()
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
