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

        private static StateUpdater<string> s_setClass;

        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        public override void SetUp()
        {
            s_setClass = default;
            s_setPoolState = default;
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

        private static StateUpdater<string> s_setPoolState;

        [Component]
        private static VNode RenderPoolReuse()
        {
            var (state, setState) = Hooks.UseState("rounded");
            s_setPoolState = setState;
            return state switch
            {
                "rounded" => V.Label(name: "leaf", className: "w-[100px] h-[40px] rounded-md rounded-[12px]", text: "a"),
                "plain" => V.Label(name: "leaf", className: "w-[100px] h-[20px]", text: "b"),
                _ => V.Div(name: "placeholder"),
            };
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

        // GREEN_ON_BASE(characterization): the base has no fit, so a recycled Label carries no radius there.
        // Dropping `CornerRadiusFit.Release(element)` from StyleArbitraryValueResolver.ClearAll reddens it: the
        // recycled Label refits the previous consumer's 12px to the 20px height it is laid out at. The class
        // beside the arbitrary radius is what makes the recycled Label resolve its style for the fit once more.
        [Test]
        public void Given_AFittedLabelReturnedToThePool_When_RentedByAPlainLabel_Then_ItPaintsNoRadius()
        {
            // Arrange — VNodePool's Label pool is LIFO and nothing else rents a Label in between, so the plain
            // label is handed the same instance back.
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderPoolReuse));
            var scheduler = _mounted.Root.Reconciler.Context.BatchScheduler;
            ForcePanelUpdate(_window.rootVisualElement.panel);
            var first = _window.rootVisualElement.Q<Label>("leaf");
            s_setPoolState.Invoke("hidden");
            scheduler.DrainImmediateForTest();

            // Act
            s_setPoolState.Invoke("plain");
            scheduler.DrainImmediateForTest();
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert — the instance is carried beside the radius, since a label built afresh has none either.
            var recycled = _window.rootVisualElement.Q<Label>("leaf");
            Assert.That((ReferenceEquals(first, recycled), recycled.resolvedStyle.borderTopLeftRadius),
                Is.EqualTo((true, 0f)));
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
        public void Given_AnInlineCornerWrittenOverAClassRadius_When_TheBoxShrinksAndGrowsBack_Then_ThatCornerKeepsItsValue()
        {
            // Arrange — the inline 4px outranks rounded-3xl on its corner, as inline style outranks a class.
            var box = MountSwitchable("w-[200px] h-[60px] rounded-3xl");
            box.style.borderTopLeftRadius = 4f;
            SwitchClass("w-[200px] h-[34px] rounded-3xl");
            var shortRadii = Radii(box);

            // Act
            SwitchClass("w-[200px] h-[60px] rounded-3xl");

            // Assert — while short, the other corners are fitted around the inline one: 24 + 24 down a 34px side
            // scales them by 34/48.
            Assert.That(new[] { shortRadii[0], shortRadii[1], box.resolvedStyle.borderTopLeftRadius },
                Is.EqualTo(new[] { 4f, 17f, 4f }).Within(1e-3f));
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
        public void Given_AnInlineCornerVelvetDidNotWrite_When_ItsNeighbourOverlapsIt_Then_OnlyTheNeighbourIsScaled()
        {
            // Arrange — 80 + 60 across a 100px top edge scales both by 100/140, but only the arbitrary corner is
            // one the fit declares; the other is written straight onto the element and left to UI Toolkit.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "box", className: "w-[100px] h-[200px] rounded-tr-[60px]"));
            Box.style.borderTopLeftRadius = 80f;

            // Act
            ForcePanelUpdate(Box.panel);

            // Assert
            var radii = Radii(Box);
            Assert.That(new[] { radii[0], radii[1] }, Is.EqualTo(new[] { 80f, 60f * 100f / 140f }).Within(1e-3f));
        }
    }
}
