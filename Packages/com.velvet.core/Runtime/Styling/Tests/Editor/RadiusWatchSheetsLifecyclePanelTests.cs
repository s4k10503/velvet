using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins how <c>RadiusWatchSheets</c> keeps the watch sheets on an element: which it takes off, which it leaves alone,
    /// when it destroys one and when it reports one it could not derive.
    /// </summary>
    internal sealed class RadiusWatchSheetsLifecyclePanelTests : PanelTestBase
    {
        private const string OwnSheet = "Packages/com.velvet.core/Runtime/Reconciler/Tests/Editor/LayoutIdTestRules.uss";
        private const BindingFlags Statics = BindingFlags.NonPublic | BindingFlags.Static;

        private readonly System.Collections.Generic.List<UnityEngine.Object> _clones = new();
        private object[] _madeBefore = Array.Empty<object>();
        private bool _reportedBefore;

        public override void SetUp()
        {
            base.SetUp();
            _reportedBefore = (bool)ReportedField().GetValue(null);
            _madeBefore = ((IList)MadeField().GetValue(null)).Cast<object>().ToArray();
        }

        public override void TearDown()
        {
            base.TearDown();
            foreach (var clone in _clones)
            {
                UnityEngine.Object.DestroyImmediate(clone);
            }
            _clones.Clear();
            ReportedField().SetValue(null, _reportedBefore);
            var made = (IList)MadeField().GetValue(null);
            made.Clear();
            foreach (var entry in _madeBefore)
            {
                made.Add(entry);
            }
        }

        private static FieldInfo ReportedField() => typeof(RadiusWatchSheets).GetField("s_reported", Statics)!;

        private static FieldInfo MadeField() => typeof(RadiusWatchSheets).GetField("s_made", Statics)!;

        private static StyleSheet Load() => UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(OwnSheet);

        private StyleSheet Clone()
        {
            var clone = UnityEngine.Object.Instantiate(Load());
            _clones.Add(clone);
            return clone;
        }

        // A copy whose every rule has lost its selectors' simple selectors, so that building a watch sheet from it fails
        // once the builder is under way.
        private StyleSheet CloneThatCannotBeBuilt()
        {
            var clone = Clone();
            var complexSelectors = EngineMember.StyleRuleComplexSelectors.ResolveProperty()!;
            var selectors = EngineMember.StyleComplexSelectorSelectors.ResolveProperty()!;
            foreach (var rule in (Array)EngineMember.StyleSheetRules.ResolveProperty()!.GetValue(clone))
            {
                foreach (var complex in (Array)complexSelectors.GetValue(rule))
                {
                    selectors.SetValue(complex, null);
                }
            }
            return clone;
        }

        private static int WatchSheetsAlive()
            => Resources.FindObjectsOfTypeAll<StyleSheet>().Count(sheet => sheet.name == "velvet-radius-watch");

        // GREEN_ON_BASE(characterization): the base leaves the sheets of an element alone when none is stale. Change `var stale = false;` to `true` in
        // DropStale and every call rewrites the list.
        [Test]
        public void Given_AnElementHoldingASourceAndItsWatchSheet_When_ItsSheetsAreEnsuredAgain_Then_NoStylePassFollows()
        {
            // Arrange
            var element = new VisualElement();
            element.AddToClassList("radius-watch-uneven");
            element.style.width = 50f;
            element.style.height = 20f;
            element.styleSheets.Add(Load());
            _window.rootVisualElement.Add(element);
            RadiusWatchSheets.EnsureOn(element);
            ForcePanelUpdate(element.panel);
            ForcePanelUpdate(element.panel);
            var passes = 0;
            element.RegisterCallback<CustomStyleResolvedEvent>(_ => passes++);

            // Act
            RadiusWatchSheets.EnsureOn(element);
            ForcePanelUpdate(element.panel);

            // Assert
            Assert.That(passes, Is.Zero);
        }

        // GREEN_ON_BASE(characterization): the base takes off a watch sheet standing first in the list. Change `i >= 0` to `i > 0` in the loop of
        // DropStale and the first sheet is never read.
        [Test]
        public void Given_AnElementHoldingAWatchSheetOfNoSourceItHolds_When_ItsSheetsAreEnsured_Then_ThatWatchSheetIsTakenOff()
        {
            // Arrange — the watch sheet is the only one the element holds, which puts it first in the list.
            var watch = RadiusWatchSheets.DerivedOf(Clone());
            var element = new VisualElement();
            element.styleSheets.Add(watch);

            // Act
            RadiusWatchSheets.EnsureOn(element);

            // Assert
            Assert.That(element.styleSheets.Contains(watch), Is.False);
        }

        // GREEN_ON_BASE(characterization): the base drops a destroyed sheet from an element. Change `held == null` to `!=` in DropStale and the
        // destroyed sheet is kept.
        [Test]
        public void Given_AnElementHoldingADestroyedStylesheetOfItsOwn_When_ItsSheetsAreEnsured_Then_ItHoldsNone()
        {
            // Arrange
            var element = new VisualElement();
            var sheet = ScriptableObject.CreateInstance<StyleSheet>();
            element.styleSheets.Add(sheet);
            UnityEngine.Object.DestroyImmediate(sheet);

            // Act
            RadiusWatchSheets.EnsureOn(element);

            // Assert
            Assert.That(element.styleSheets.count, Is.Zero);
        }

        // GREEN_ON_BASE(characterization): the base derives no sheet from a source edited to declare no radius. Drop `derived.Sheet = null;` from the
        // try of DerivedOf and the earlier sheet is returned.
        [Test]
        public void Given_ASourceEditedInPlaceToDeclareNoRadius_When_ItsWatchSheetIsDerivedAgain_Then_ThereIsNone()
        {
            // Arrange
            var source = Clone();
            var before = RadiusWatchSheets.DerivedOf(source);
            var rules = EngineMember.StyleSheetRules.ResolveProperty()!;
            typeof(StyleSheet).GetField("m_Rules", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(source, Array.CreateInstance(rules.PropertyType.GetElementType()!, 0));
            source.contentHash += 1;

            // Act
            var after = RadiusWatchSheets.DerivedOf(source);

            // Assert — a first sheet never made reads false, whatever the second is.
            Assert.That((object)before != null && (object)after == null, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base destroys a sheet whose build failed. Change `made != null` to `== null` in the catch of DerivedOf
        // and the sheet is left alive.
        [Test]
        public void Given_ASourceWhoseWatchSheetCannotBeBuilt_When_ItIsAskedFor_Then_ThereIsNoneAndNoSheetIsLeftBehind()
        {
            // Arrange
            var source = CloneThatCannotBeBuilt();
            var before = WatchSheetsAlive();

            // Act
            RadiusWatchSheets.DerivedOf(source);

            // Assert
            Assert.That(WatchSheetsAlive() - before, Is.Zero);
        }

        // GREEN_ON_BASE(characterization): the base logs the warning for the first sheet it cannot build. Drop the `Debug.LogWarning` call from the
        // catch of DerivedOf and the warning the case expects never arrives.
        [Test]
        public void Given_ASourceWhoseWatchSheetCannotBeBuilt_When_ItIsAskedForAndNoneWasReportedYet_Then_OneIsReported()
        {
            // Arrange
            ReportedField().SetValue(null, false);
            var source = CloneThatCannotBeBuilt();
            LogAssert.Expect(LogType.Warning, new Regex("could not derive a radius watch sheet"));

            // Act
            RadiusWatchSheets.DerivedOf(source);

            // Assert
            Assert.That((bool)ReportedField().GetValue(null), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base destroys the only sheet made once its source is gone. Change `i >= 0` to `i > 0` in DestroyOrphans
        // and the sheet at the first place is kept.
        [Test]
        public void Given_TheOnlyWatchSheetMadeAndItsSourceDestroyed_When_AnElementsSheetsAreEnsured_Then_ItIsDestroyed()
        {
            // Arrange — the list of sheets made holds this one alone, so it sits at its first place.
            ((IList)MadeField().GetValue(null)).Clear();
            var source = Clone();
            var watch = RadiusWatchSheets.DerivedOf(source);
            var made = (object)watch != null;
            UnityEngine.Object.DestroyImmediate(source);

            // Act
            RadiusWatchSheets.EnsureOn(new VisualElement());

            // Assert — the engine's null check holds a destroyed sheet to be none; a sheet never made reads false.
            Assert.That(made && watch == null, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base names each sheet it makes. Drop `sheet.name = SheetName;` from NewSheet and a sheet known by no
        // reference is no watch sheet.
        [Test]
        public void Given_AWatchSheetRememberedByNoReference_When_ItIsAskedWhetherItIsOne_Then_ItsNameSaysItIs()
        {
            // Arrange — a sheet that outlived a domain reload is known by no reference.
            var watch = RadiusWatchSheets.DerivedOf(Clone());
            var known = typeof(RadiusWatchSheets).GetField("s_known", Statics)!.GetValue(null)!;
            known.GetType().GetMethod("Remove")!.Invoke(known, new object[] { watch });

            // Act
            var isWatch = (object)watch != null && RadiusWatchSheets.IsWatch(watch);

            // Assert
            Assert.That(isWatch, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base attaches the watch sheets of the sheets a root already holds. Drop
        // `RadiusWatchSheets.EnsureAbove(root);` from VelvetStyleUtilities.AttachTo and the root holds none.
        [Test]
        public void Given_ASheetOnARootBeforeTheUtilitiesAreAttached_When_TheyAreAttached_Then_TheRootHoldsTheSheetsWatchSheet()
        {
            // Arrange
            var source = Load();
            _window.rootVisualElement.styleSheets.Add(source);

            // Act
            VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

            // Assert
            var watch = RadiusWatchSheets.DerivedOf(source);
            Assert.That(watch != null && _window.rootVisualElement.styleSheets.Contains(watch), Is.True);
        }
    }
}
