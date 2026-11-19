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

        private readonly List<UnityEngine.Object> _clones = new();

        public override void TearDown()
        {
            base.TearDown();
            foreach (var clone in _clones)
            {
                UnityEngine.Object.DestroyImmediate(clone);
            }
            _clones.Clear();
        }

        // A copy of the sheet no other test shares, which a case may edit.
        private StyleSheet Clone(string path)
        {
            var clone = UnityEngine.Object.Instantiate(Load(path));
            _clones.Add(clone);
            return clone;
        }

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

        // One tick of the panel's watch over the stylesheets of its ancestors, which BorderPreflight owns.
        private static void TickSheetWatch(IPanel panel)
        {
            var table = typeof(BorderPreflight).GetField("s_sheetWatches", Statics)!.GetValue(null)!;
            var args = new object[] { panel, null! };
            table.GetType().GetMethod("TryGetValue")!.Invoke(table, args);
            typeof(BorderPreflight).GetMethod("TickSheets", Statics)!.Invoke(null, new[] { panel, args[1] });
        }

        [Test]
        public void Given_AUserSheetAboveAMount_When_TheTreeMounts_Then_TheRootHoldsItsWatchSheetWithARulePerRadiusRule()
        {
            // Arrange
            var source = Load(OwnSheet);
            _window.rootVisualElement.styleSheets.Add(source);

            // Act
            MountAndSettle();

            // Assert — a sheet the root does not hold counts -1, which no number of rules equals.
            var watch = RadiusWatchSheets.DerivedOf(source);
            var held = watch != null && _window.rootVisualElement.styleSheets.Contains(watch);
            Assert.That(held ? PropertyNamesPerRule(watch).Count() : -1,
                Is.EqualTo(PropertyNamesPerRule(source).Count(DeclaresRadius)).And.GreaterThan(0));
        }

        [Test]
        public void Given_AUserSheetAboveAMount_When_TheTreeMounts_Then_EveryRuleOfItsWatchSheetHoldsOnlyTheWatchProperty()
        {
            // Arrange
            var source = Load(OwnSheet);
            _window.rootVisualElement.styleSheets.Add(source);

            // Act
            MountAndSettle();

            // Assert — a run with no watch rule at all reads -1, which no count of wrong rules equals.
            var watch = RadiusWatchSheets.DerivedOf(source);
            var rules = watch == null ? new List<string[]>() : PropertyNamesPerRule(watch).ToList();
            Assert.That(rules.Count == 0 ? -1 : rules.Count(names => names.Length != 1 || names[0] != Watch), Is.Zero);
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

        // GREEN_ON_BASE(characterization): the base derives no watch sheet from any sheet.
        [Test]
        public void Given_ASheetWithNoRadiusRule_When_ItsWatchSheetIsAsked_Then_ThereIsNone()
        {
            // Act
            var watch = RadiusWatchSheets.DerivedOf(Load(NoRadiusSheet));

            // Assert
            Assert.That(watch, Is.Null);
        }

        [Test]
        public void Given_AUserSheetAboveAMount_When_TheTreeMounts_Then_NoRuleOfItsWatchSheetCountsAsDeclaringAVariable()
        {
            // Arrange
            var source = Load(OwnSheet);
            _window.rootVisualElement.styleSheets.Add(source);
            MountAndSettle();
            var watch = RadiusWatchSheets.DerivedOf(source);
            var count = EngineMember.StyleRuleCustomPropertiesCount.ResolveField()!;

            // Act
            var counted = watch == null
                ? new List<int> { -1 }
                : ((Array)EngineMember.StyleSheetRules.ResolveProperty()!.GetValue(watch)).Cast<object>()
                    .Select(rule => (int)count.GetValue(rule)).ToList();

            // Assert — a missing sheet reads -1, which no count of variables equals.
            Assert.That(counted.Max(), Is.Zero);
        }

        // GREEN_ON_BASE(characterization): the base attaches no watch sheet to any root.
        [Test]
        public void Given_OnlyTheBundledUtilitiesAboveAMount_When_TheTreeMounts_Then_TheirWatchSheetIsNotAttached()
        {
            // Arrange
            var bundled = Load(StylesDirectory + "/StyleUtilities.uss");

            // Act
            MountAndSettle();

            // Assert
            var watch = RadiusWatchSheets.DerivedOf(bundled);
            Assert.That(watch != null && _window.rootVisualElement.styleSheets.Contains(watch), Is.False);
        }

        [Test]
        public void Given_AMountedTree_When_ASheetWithARadiusRuleIsAttachedToItsRootAfterwards_Then_ThePanelsWatchAttachesItsWatchSheet()
        {
            // Arrange — the tree below the root has laid out, which is what has the panel's watch compare the root's sheets.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "box", className: "w-[100px] h-[40px]"));
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Act
            var source = Load(OwnSheet);
            _window.rootVisualElement.styleSheets.Add(source);
            TickSheetWatch(_window.rootVisualElement.panel);

            // Assert
            var watch = RadiusWatchSheets.DerivedOf(source);
            Assert.That(watch != null && _window.rootVisualElement.styleSheets.Contains(watch), Is.True);
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
            var source = Load(OwnSheet);
            inner.styleSheets.Add(source);
            TickSheetWatch(inner.panel);

            // Assert
            var watch = RadiusWatchSheets.DerivedOf(source);
            Assert.That(watch != null && inner.styleSheets.Contains(watch), Is.True);
        }

        [Test]
        public void Given_AnAttachedSourceEditedInPlace_When_ThePanelsWatchTicks_Then_ItsNewWatchSheetReplacesTheOld()
        {
            // Arrange — a copy, whose content hash moves as an edit of the sheet in place moves it.
            var source = Clone(OwnSheet);
            _window.rootVisualElement.styleSheets.Add(source);
            MountAndSettle();
            var before = RadiusWatchSheets.DerivedOf(source);

            // Act
            source.contentHash += 1;
            TickSheetWatch(_window.rootVisualElement.panel);

            // Assert
            var after = RadiusWatchSheets.DerivedOf(source);
            Assert.That(after != null && !ReferenceEquals(after, before) && _window.rootVisualElement.styleSheets.Contains(after)
                && !_window.rootVisualElement.styleSheets.Contains(before), Is.True);
        }

        [Test]
        public void Given_AnAttachedSourceEditedInPlace_When_ItsWatchSheetIsDerivedAgain_Then_NoRuleOfTheNewOneCountsAsDeclaringAVariable()
        {
            // Arrange
            var source = Clone(OwnSheet);
            _window.rootVisualElement.styleSheets.Add(source);
            MountAndSettle();
            source.contentHash += 1;

            // Act
            var watch = RadiusWatchSheets.DerivedOf(source);

            // Assert — a missing sheet reads -1, which no count of variables equals.
            var count = EngineMember.StyleRuleCustomPropertiesCount.ResolveField()!;
            var counted = watch == null
                ? new List<int> { -1 }
                : ((Array)EngineMember.StyleSheetRules.ResolveProperty()!.GetValue(watch)).Cast<object>()
                    .Select(rule => (int)count.GetValue(rule)).ToList();
            Assert.That(counted.Max(), Is.Zero);
        }

        [Test]
        public void Given_AnAttachedSource_When_ItIsRemovedFromTheRootAndThePanelsWatchTicks_Then_ItsWatchSheetIsTakenOff()
        {
            // Arrange
            var source = Clone(OwnSheet);
            _window.rootVisualElement.styleSheets.Add(source);
            MountAndSettle();
            var watch = RadiusWatchSheets.DerivedOf(source);

            // Act
            _window.rootVisualElement.styleSheets.Remove(source);
            TickSheetWatch(_window.rootVisualElement.panel);

            // Assert
            Assert.That(watch != null && !_window.rootVisualElement.styleSheets.Contains(watch), Is.True);
        }

        [Test]
        public void Given_AWatchSheetDestroyedSinceItWasMade_When_ItIsAskedWhetherItIsOne_Then_ItIsOne()
        {
            // Arrange
            var watch = RadiusWatchSheets.DerivedOf(Clone(OwnSheet));
            UnityEngine.Object.DestroyImmediate(watch);

            // Act
            var known = (object)watch != null && RadiusWatchSheets.IsWatch(watch);

            // Assert — the engine's null check holds a destroyed sheet to be none, so the reference is what is asked.
            Assert.That(known, Is.True);
        }

        [Test]
        public void Given_ASourceDestroyedWhileItsWatchSheetIsAttached_When_TheRootIsReadAndThePanelsWatchTicks_Then_ThatWatchSheetIsNoLongerListed()
        {
            // Arrange — the window's root also holds watch sheets of the editor's own sheets, so the sheet asked after is this
            // one, by reference, which stays the same object once destroyed.
            var source = Clone(OwnSheet);
            _window.rootVisualElement.styleSheets.Add(source);
            MountAndSettle();
            var watch = RadiusWatchSheets.DerivedOf(source);
            var made = (object)watch != null;
            UnityEngine.Object.DestroyImmediate(source);

            // Act
            RadiusWatchSheets.EnsureOn(_window.rootVisualElement);
            TickSheetWatch(_window.rootVisualElement.panel);

            // Assert
            var listed = 0;
            for (var i = 0; i < _window.rootVisualElement.styleSheets.count; i++)
            {
                listed += ReferenceEquals(_window.rootVisualElement.styleSheets[i], watch) ? 1 : 0;
            }
            // A sheet never made, or one still alive, reads -1.
            Assert.That(made && watch == null ? listed : -1, Is.Zero);
        }

        [Test]
        public void Given_ALeafHoldingASheetOfItsOwn_When_ItLaysOut_Then_ItHoldsTheWatchSheet()
        {
            // Arrange — the leaf has no children, so only a read of its own sheets can find the rule.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "leaf", className: "w-[100px] h-[40px]"));
            var leaf = _window.rootVisualElement.Q<VisualElement>("leaf");
            var source = Load(OwnSheet);
            leaf.styleSheets.Add(source);

            // Act
            ForcePanelUpdate(leaf.panel);

            // Assert
            var watch = RadiusWatchSheets.DerivedOf(source);
            Assert.That(watch != null && leaf.styleSheets.Contains(watch), Is.True);
        }

        [Test]
        public void Given_AContainerWhoseChildLaidOutBeforeItWasReleased_When_ItHoldsASheetAndAChildLaysOutAgain_Then_ItHoldsTheWatchSheet()
        {
            // Arrange — a container the fit read once, then released as a pooled element is, which another chain then adopts.
            var container = new VisualElement();
            _window.rootVisualElement.Add(container);
            var first = new VisualElement();
            first.style.width = 50f;
            first.style.height = 20f;
            CornerRadiusFit.Observe(first);
            container.Add(first);
            ForcePanelUpdate(_window.rootVisualElement.panel);
            CornerRadiusFit.Release(container);
            var source = Load(OwnSheet);
            container.styleSheets.Add(source);
            var second = new VisualElement();
            second.style.width = 60f;
            second.style.height = 20f;
            CornerRadiusFit.Observe(second);

            // Act
            container.Add(second);
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            var watch = RadiusWatchSheets.DerivedOf(source);
            Assert.That(watch != null && container.styleSheets.Contains(watch), Is.True);
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
            // A selector with an escaped character, as a variant class has, is read whole on both sides.
            var cls = new Regex(@"\.((?:\\.|[A-Za-z0-9_-])+)");
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
