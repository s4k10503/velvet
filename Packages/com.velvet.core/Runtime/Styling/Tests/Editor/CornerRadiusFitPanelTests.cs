using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the corner radii <c>CornerRadiusFit</c> leaves on an element laid out on a real panel with the
    /// bundled stylesheet attached, read back through <c>resolvedStyle</c>.
    /// </summary>
    /// <remarks>
    /// The stylesheet matters twice over: it is what declares <c>rounded-*</c>, and what restates that
    /// declaration as the custom properties the fit reads.
    /// </remarks>
    internal sealed class CornerRadiusFitPanelTests : PanelTestBase
    {
        private const string WideBox = "w-[330px] h-[34px]";

        private const string OwnRadiusSheet = "Packages/com.velvet.core/Runtime/Reconciler/Tests/Editor/LayoutIdTestRules.uss";

        private static StateUpdater<string> s_setClass;

        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        public override void SetUp()
        {
            s_setClass = default;
            base.SetUp();
        }

        private VisualElement Box => _window.rootVisualElement.Q<VisualElement>("box");

        private VisualElement MountBox(string className)
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "box", className: className));
            ForcePanelUpdate(Box.panel);
            return Box;
        }

        // Mounts a box whose class list s_setClass replaces, starting from initialClass.
        private VisualElement MountSwitchable(string initialClass)
        {
            s_initialClass = initialClass;
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderSwitchable));
            ForcePanelUpdate(Box.panel);
            return Box;
        }

        private void SwitchClass(string className)
        {
            s_setClass.Invoke(className);
            _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();
            ForcePanelUpdate(Box.panel);
        }

        private static string s_initialClass;

        [Component]
        private static VNode RenderSwitchable()
        {
            var (className, setClass) = Hooks.UseState(s_initialClass);
            s_setClass = setClass;
            return V.Div(name: "box", className: className);
        }

        private static float[] Radii(VisualElement element)
        {
            var style = element.resolvedStyle;
            return new[]
            {
                style.borderTopLeftRadius, style.borderTopRightRadius,
                style.borderBottomRightRadius, style.borderBottomLeftRadius,
            };
        }

        private static bool IsNear(float actual, float expected) => Mathf.Abs(actual - expected) < 1e-3f;

        [Test]
        public void Given_AWideRoundedFullBox_When_LaidOut_Then_EveryCornerTakesHalfTheHeight()
        {
            // Arrange / Act
            var box = MountBox($"{WideBox} rounded-full");

            // Assert
            Assert.That(Radii(box), Is.EqualTo(new[] { 17f, 17f, 17f, 17f }).Within(1e-3f));
        }

        [Test]
        public void Given_ARoundedFullBox_When_ItsClassBecomesRoundedMd_Then_EveryCornerTakesTheMdRadius()
        {
            // Arrange — the fitted radius is read first: it is the value the element held inline, where the
            // declared one it hid is the one the class change replaces.
            var box = MountSwitchable($"{WideBox} rounded-full");
            var fitted = box.resolvedStyle.borderTopLeftRadius;

            // Act
            SwitchClass($"{WideBox} rounded-md");

            // Assert
            var after = Radii(box);
            Assert.That(new[] { fitted, after[0], after[1], after[2], after[3] },
                Is.EqualTo(new[] { 17f, 6f, 6f, 6f, 6f }).Within(1e-3f));
        }

        [Test]
        public void Given_AnArbitraryRightRadiusBeyondTheHeight_When_LaidOut_Then_TheRightCornersTakeHalfTheHeight()
        {
            // Arrange / Act
            var box = MountBox("w-[200px] h-[75px] rounded-r-[400px]");

            // Assert
            Assert.That(Radii(box), Is.EqualTo(new[] { 0f, 37.5f, 37.5f, 0f }).Within(1e-3f));
        }

        [Test]
        public void Given_APercentRadiusBeyondHalf_When_LaidOut_Then_ItIsScaledInPercent()
        {
            // Arrange / Act — 75% on each corner overlaps by half on every side, so each is scaled by 2/3.
            // resolvedStyle reports a percentage as its number, so 50 is 50%.
            var box = MountBox($"{WideBox} rounded-[75%]");

            // Assert
            Assert.That(box.resolvedStyle.borderTopLeftRadius, Is.EqualTo(50f).Within(1e-3f));
        }

        [Test]
        public void Given_ARounded3xlBoxShorterThanItsRadius_When_ItGrowsToFitIt_Then_TheSlotIsHandedBackToTheStylesheet()
        {
            // Arrange
            var box = MountSwitchable("w-[200px] h-[34px] rounded-3xl");
            var fittedWhileShort = IsNear(box.resolvedStyle.borderTopLeftRadius, 17f);

            // Act
            SwitchClass("w-[200px] h-[60px] rounded-3xl");

            // Assert — the stylesheet's 24px is what then shows, but only an empty inline slot says it is the
            // stylesheet showing it rather than a held copy of the same value.
            Assert.That((fittedWhileShort, box.style.borderTopLeftRadius.keyword),
                Is.EqualTo((true, StyleKeyword.Null)));
        }

        [Test]
        public void Given_ATransitionAllRoundedFullBox_When_FirstLaidOut_Then_ItsCornersLandAtHalfTheHeightAtOnce()
        {
            // Arrange — the clock is held still, so a transition started by the fit could not have progressed.
            EditorPanelTestHelpers.SetPanelTimeFunction(_window.rootVisualElement.panel, () => 100.0);

            // Act
            var box = MountBox($"{WideBox} rounded-full transition-all");

            // Assert
            Assert.That(box.resolvedStyle.borderTopLeftRadius, Is.EqualTo(17f).Within(1e-3f));
        }

        [Test]
        public void Given_ATransitionAllRoundedFullBox_When_FirstLaidOut_Then_ItsTransitionsAreHandedBack()
        {
            // Arrange
            EditorPanelTestHelpers.SetPanelTimeFunction(_window.rootVisualElement.panel, () => 100.0);

            // Act
            var box = MountBox($"{WideBox} rounded-full transition-all");

            // Assert — the fitted radius is carried beside the slot, since an element the fit never wrote to
            // has an empty transition slot as well.
            Assert.That((IsNear(box.resolvedStyle.borderTopLeftRadius, 17f), box.style.transitionProperty.keyword),
                Is.EqualTo((true, StyleKeyword.Null)));
        }

        [Test]
        public void Given_ATransitionAllRoundedFullBox_When_ItsClassBecomesRoundedMd_Then_TheChangeTransitions()
        {
            // Arrange — with the clock held still a transition stays at its first value, so a radius still above
            // the md radius means the change started one rather than landing.
            EditorPanelTestHelpers.SetPanelTimeFunction(_window.rootVisualElement.panel, () => 100.0);
            var box = MountSwitchable($"{WideBox} rounded-full transition-all");
            var fitted = IsNear(box.resolvedStyle.borderTopLeftRadius, 17f);

            // Act
            SwitchClass($"{WideBox} rounded-md transition-all");

            // Assert
            Assert.That((fitted, box.resolvedStyle.borderTopLeftRadius > 6.5f), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_TwoArbitraryTopCornersThatOverlap_When_OneIsRemoved_Then_TheOtherReturnsToItsDeclaredRadius()
        {
            // Arrange — 60 + 60 across a 100px top edge fits each corner to 50.
            var box = MountSwitchable("w-[100px] h-[200px] rounded-tl-[60px] rounded-tr-[60px]");
            var fitted = box.resolvedStyle.borderTopRightRadius;

            // Act
            SwitchClass("w-[100px] h-[200px] rounded-tr-[60px]");

            // Assert
            var after = Radii(box);
            Assert.That(new[] { fitted, after[0], after[1] }, Is.EqualTo(new[] { 50f, 0f, 60f }).Within(1e-3f));
        }

        [Test]
        public void Given_AnInlineTransitionPropertyOnARoundedFullBox_When_ItsHeightChanges_Then_TheTransitionPropertyIsRestored()
        {
            // Arrange — a height change refits the corners with transitions suspended.
            var box = MountBox($"{WideBox} rounded-full");
            box.style.transitionProperty = new List<StylePropertyName> { new StylePropertyName("background-color") };

            // Act
            box.style.height = 40f;
            ForcePanelUpdate(box.panel);

            // Assert — the refit is carried beside the list, since a box the fit never wrote to keeps it too.
            var names = string.Join(",", box.style.transitionProperty.value);
            Assert.That((IsNear(box.resolvedStyle.borderTopLeftRadius, 20f), names),
                Is.EqualTo((true, "background-color")));
        }

        [Test]
        public void Given_AWideBox_When_AnAnimationClassChannelAddsRoundedFull_Then_EveryCornerTakesHalfTheHeight()
        {
            // Arrange — whileHover/Tap/Focus classes, the animation scheduler's classes and drag classes reach the
            // element through this helper rather than through the class projection.
            var box = MountBox(WideBox);

            // Act
            StyleAnimationClassUtils.AddClasses(box, new[] { "rounded-full" });
            ForcePanelUpdate(box.panel);

            // Assert
            Assert.That(box.resolvedStyle.borderTopLeftRadius, Is.EqualTo(17f).Within(1e-3f));
        }

        [Test]
        public void Given_AnInlineCornerWrittenOverAClassRadius_When_TheBoxShrinksAndGrowsBack_Then_ThatCornerIsScaledWithTheOthersAndComesBack()
        {
            // Arrange — the inline 4px outranks rounded-3xl on its corner, as inline style outranks a class.
            var box = MountSwitchable("w-[200px] h-[60px] rounded-3xl");
            box.style.borderTopLeftRadius = 4f;
            SwitchClass("w-[200px] h-[34px] rounded-3xl");
            var shortRadii = Radii(box);

            // Act
            SwitchClass("w-[200px] h-[60px] rounded-3xl");

            // Assert — while short, 24 + 24 down a 34px side scales every corner by 34/48, the inline one included.
            Assert.That(new[] { shortRadii[0], shortRadii[1], box.resolvedStyle.borderTopLeftRadius },
                Is.EqualTo(new[] { 4f * 34f / 48f, 17f, 4f }).Within(1e-3f));
        }

        [Test]
        public void Given_AnInlineKeywordWrittenOverAClassRadius_When_TheBoxShrinksAndGrowsBack_Then_ThatCornerKeepsTheKeyword()
        {
            // Arrange — Initial resolves the corner to 0 and outranks rounded-3xl there.
            var box = MountSwitchable("w-[200px] h-[60px] rounded-3xl");
            box.style.borderTopLeftRadius = StyleKeyword.Initial;
            SwitchClass("w-[200px] h-[34px] rounded-3xl");
            var shortRadius = box.resolvedStyle.borderTopLeftRadius;
            var neighbourFitted = IsNear(box.resolvedStyle.borderTopRightRadius, 17f);

            // Act
            SwitchClass("w-[200px] h-[60px] rounded-3xl");

            // Assert — the radius while short is carried beside the slot: putting the keyword back afterwards
            // would still have painted the fitted 17 meanwhile. The neighbour, fitted by 34/48 while short, says
            // the fit ran at all.
            Assert.That((shortRadius, neighbourFitted, box.style.borderTopLeftRadius.keyword),
                Is.EqualTo((0f, true, StyleKeyword.Initial)));
        }

        [Test]
        public void Given_AClassCornerFittedBesideAnArbitraryOne_When_TheClassIsRemoved_Then_TheArbitraryCornerTakesItsDeclaredRadius()
        {
            // Arrange — 9999 + 60 across a 100px top edge scales both by 100/10059.
            var box = MountSwitchable("w-[100px] h-[200px] rounded-tl-full rounded-tr-[60px]");
            var fitted = box.resolvedStyle.borderTopRightRadius;

            // Act — the class leaves on a style pass of its own, after the arbitrary radius was last applied.
            SwitchClass("w-[100px] h-[200px] rounded-tr-[60px]");

            // Assert — refitting the arbitrary corner against the class corner's still-held radius leaves it at
            // 100/159.4 of 60.
            var after = Radii(box);
            Assert.That(new[] { fitted, after[0], after[1] },
                Is.EqualTo(new[] { 60f * 100f / 10059f, 0f, 60f }).Within(1e-3f));
        }

        [Test]
        public void Given_AnInlineCornerVelvetDidNotWrite_When_ItsNeighbourOverlaps_Then_BothAreScaled()
        {
            // Arrange — 80 + 60 across a 100px top edge scales both by 100/140; one corner is the arbitrary class's
            // and the other is written straight onto the element.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "box", className: "w-[100px] h-[200px] rounded-tr-[60px]"));
            Box.style.borderTopLeftRadius = 80f;

            // Act
            ForcePanelUpdate(Box.panel);

            // Assert
            var radii = Radii(Box);
            Assert.That(new[] { radii[0], radii[1] }, Is.EqualTo(new[] { 80f * 100f / 140f, 60f * 100f / 140f }).Within(1e-3f));
        }

        [Test]
        public void Given_ABoxWithNoRoundedClassWhoseCodeWritesARadiusBeyondItsHeight_When_LaidOut_Then_EveryCornerTakesHalfTheHeight()
        {
            // Arrange — no class on the element declares a radius, and 400 on each corner overlaps on every side.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "box", className: WideBox));
            Box.style.borderTopLeftRadius = 400f;
            Box.style.borderTopRightRadius = 400f;
            Box.style.borderBottomRightRadius = 400f;
            Box.style.borderBottomLeftRadius = 400f;

            // Act
            ForcePanelUpdate(Box.panel);

            // Assert
            Assert.That(Radii(Box), Is.EqualTo(new[] { 17f, 17f, 17f, 17f }).Within(1e-3f));
        }

        [Test]
        public void Given_ABoxWhoseOwnStylesheetGivesItARadiusBeyondItsHeight_When_LaidOut_Then_ItsCornersAreScaledToFitIt()
        {
            // Arrange — the rule gives the top-left corner 90 and the top-right 30, the radius the application's own
            // sheet declares and no Velvet class names.
            _window.rootVisualElement.styleSheets.Add(
                UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(OwnRadiusSheet));

            // Act
            var box = MountBox($"{WideBox} layout-id-test-uneven");

            // Assert — 90 down a 34px side scales every corner by 34/90.
            Assert.That(Radii(box), Is.EqualTo(new[] { 34f, 30f * 34f / 90f, 0f, 0f }).Within(1e-3f));
        }

        [Test]
        public void Given_ABoxFittedFromItsOwnStylesheetRadius_When_ItGrowsToFitIt_Then_TheSlotIsHandedBackToTheStylesheet()
        {
            // Arrange
            _window.rootVisualElement.styleSheets.Add(
                UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(OwnRadiusSheet));
            var box = MountSwitchable($"{WideBox} layout-id-test-uneven");
            var fittedWhileShort = IsNear(box.resolvedStyle.borderTopLeftRadius, 34f);

            // Act — 90 down a 420px side fits unscaled.
            SwitchClass("w-[420px] h-[420px] layout-id-test-uneven");

            // Assert
            Assert.That((fittedWhileShort, box.style.borderTopLeftRadius.keyword, box.resolvedStyle.borderTopLeftRadius),
                Is.EqualTo((true, StyleKeyword.Null, 90f)));
        }
    }
}
