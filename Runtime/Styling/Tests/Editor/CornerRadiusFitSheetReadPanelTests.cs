using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins when <c>CornerRadiusFit</c> reads the stylesheets above an element and its own rules' radii, on a real panel.
    /// </summary>
    internal sealed class CornerRadiusFitSheetReadPanelTests : PanelTestBase
    {
        private const string OwnSheet = "Packages/com.velvet.core/Runtime/Reconciler/Tests/Editor/LayoutIdTestRules.uss";
        private const BindingFlags Statics = BindingFlags.NonPublic | BindingFlags.Static;

        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        private static StyleSheet LoadOwn() => UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(OwnSheet);

        // A size event that leaves the size as it was, which reads the sheets above and nothing else.
        private static void GeometryChangedWithoutResizing(VisualElement element)
        {
            var rect = new Rect(0f, 0f, 10f, 10f);
            using var evt = GeometryChangedEvent.GetPooled(rect, rect);
            CornerRadiusFit.OnGeometryChanged(element, evt);
        }

        private (VisualElement Parent, VisualElement First, VisualElement Second) ParentHoldingASheetWithTwoChildren()
        {
            var parent = new VisualElement();
            parent.styleSheets.Add(LoadOwn());
            var first = new VisualElement();
            var second = new VisualElement();
            parent.Add(first);
            parent.Add(second);
            _window.rootVisualElement.Add(parent);
            return (parent, first, second);
        }

        private static bool IsWatchedAsAncestor(VisualElement element)
        {
            var table = typeof(BorderPreflight).GetField("s_sheetWatches", Statics)!.GetValue(null)!;
            var args = new object[] { element.panel, null! };
            if (!(bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table, args)!)
            {
                return false;
            }
            var watch = args[1];
            var known = watch.GetType().GetField("Known")!.GetValue(watch)!;
            var probe = new object[] { element, null! };
            return (bool)known.GetType().GetMethod("TryGetValue")!.Invoke(known, probe)!;
        }

        [Test]
        public void Given_AnElementWithNoStylesheetOfItsOwn_When_ItsSizeEventIsRead_Then_ItIsNotWatchedAsAnAncestor()
        {
            // Arrange
            var (_, first, _) = ParentHoldingASheetWithTwoChildren();

            // Act
            GeometryChangedWithoutResizing(first);

            // Assert
            Assert.That(IsWatchedAsAncestor(first), Is.False);
        }

        [Test]
        public void Given_AParentHoldingASheetWithARadiusRule_When_AChildsSizeEventIsRead_Then_TheParentHoldsTheWatchSheet()
        {
            // Arrange
            var (parent, first, _) = ParentHoldingASheetWithTwoChildren();

            // Act
            GeometryChangedWithoutResizing(first);

            // Assert
            var watch = RadiusWatchSheets.DerivedOf(LoadOwn());
            Assert.That(watch != null && parent.styleSheets.Contains(watch), Is.True);
        }

        [Test]
        public void Given_AParentWhoseSheetsWereLookedAtForOneChild_When_ASiblingsSizeEventIsRead_Then_TheyAreNotLookedAtAgain()
        {
            // Arrange — the watch sheet is taken off between the two events, which the first would put back if it ran again.
            var (parent, first, second) = ParentHoldingASheetWithTwoChildren();
            GeometryChangedWithoutResizing(first);
            var watch = RadiusWatchSheets.DerivedOf(LoadOwn());
            parent.styleSheets.Remove(watch);

            // Act
            GeometryChangedWithoutResizing(second);

            // Assert
            Assert.That(watch != null && parent.styleSheets.Contains(watch), Is.False);
        }

        [Test]
        public void Given_ABoxFittedFromItsOwnSheetWhoseCodeWroteAnOverflowingRadius_When_ItsStylePassMatchedTheSameRules_Then_ThatRadiusIsLeftAsWritten()
        {
            // Arrange
            _window.rootVisualElement.styleSheets.Add(LoadOwn());
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "box", className: "w-[330px] h-[34px] radius-watch-uneven"));
            var box = _window.rootVisualElement.Q<VisualElement>("box");
            ForcePanelUpdate(box.panel);
            CornerRadiusFit.OnStyleResolved(box);
            box.style.borderBottomRightRadius = 400f;

            // Act
            CornerRadiusFit.OnStyleResolved(box);

            // Assert — a box the fit does not track reads NaN, which no radius equals.
            Assert.That(CornerRadiusFit.Fitted(box, 0) != null ? box.style.borderBottomRightRadius.value.value : float.NaN,
                Is.EqualTo(400f));
        }

        [Test]
        public void Given_AnElementHoldingACodeWrittenRadius_When_AClassIsWrittenThroughVelvet_Then_ItsRulesRadiusIsReadAtOnce()
        {
            // Arrange — no style pass follows the write, so only a read of the cascade can find the rule's radius.
            _window.rootVisualElement.styleSheets.Add(LoadOwn());
            var element = new VisualElement();
            element.AddToClassList("radius-watch-uneven");
            element.style.width = 330f;
            element.style.height = 200f;
            element.style.borderBottomLeftRadius = 20f;
            _window.rootVisualElement.Add(element);
            ForcePanelUpdate(element.panel);

            // Act
            CornerRadiusFit.NoteClassWrite(element);

            // Assert — the top-right corner is the rule's 30, and reads NaN where the fit has none.
            Assert.That(CornerRadiusFit.Fitted(element, 1)?.value ?? float.NaN, Is.EqualTo(30f).Within(1e-3f));
        }
    }
}
